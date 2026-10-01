using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDeviceContextSyncService
{
    DeviceContextSyncStatus GetStatus();
    IReadOnlyList<DeviceContextSyncPackage> GetAll();
    DeviceContextSyncPackage? Get(Guid syncId);
    Task<DeviceContextSyncPackage> CreateAsync(
        Guid correlationId,
        CreateDeviceContextSyncRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DeviceContextSyncService(
    IDistributedAgentExecutionService distributed,
    IPersonalMemoryGroundingService memoryGrounding,
    IPersonalTaskStore tasks,
    ILifeContextService lifeContext,
    IDeviceHubService hub,
    IDeviceCapabilityService capabilities,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IDeviceContextSyncService
{
    public const int DefaultMaximumCharacters = 3_000;
    public const int HardMaximumCharacters = 6_000;
    public const int MaximumMemoryCharacters = 1_500;
    public const int MaximumLifeContextCharacters = 1_200;
    public const int MaximumTaskCharacters = 1_500;
    public const int MaximumLifeContextEntries = 4;

    private readonly object _gate = new();
    private readonly List<DeviceContextSyncPackage> _packages = [];

    public DeviceContextSyncStatus GetStatus() =>
        new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            ExplicitConfirmationRequired: true,
            FullMemorySyncEnabled: false,
            QueryScopedSelection: true,
            DeviceRouteBindingRequired: true,
            OnlineDeviceRequired: true,
            CapabilityPermissionRevalidated: true,
            DefaultMaximumCharacters,
            HardMaximumCharacters,
            DeviceContextKinds.All.Order(StringComparer.Ordinal).ToArray());

    public IReadOnlyList<DeviceContextSyncPackage> GetAll()
    {
        lock (_gate)
            return _packages
                .Where(x => x.WorkspaceId == workspace.CurrentWorkspaceId)
                .OrderByDescending(x => x.CreatedAt)
                .ToArray();
    }

    public DeviceContextSyncPackage? Get(Guid syncId)
    {
        lock (_gate)
            return _packages.FirstOrDefault(x =>
                x.SyncId == syncId &&
                x.WorkspaceId == workspace.CurrentWorkspaceId);
    }

    public async Task<DeviceContextSyncPackage> CreateAsync(
        Guid correlationId,
        CreateDeviceContextSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmSync)
            throw new DeviceContextSyncValidationException(
                "Cần ConfirmSync=true trước khi gửi context sang thiết bị khác.");

        var query = (request.Query ?? string.Empty).Trim();
        if (query.Length is < 2 or > 2_000)
            throw new DeviceContextSyncValidationException(
                "Query context phải có từ 2 đến 2.000 ký tự.");

        var execution = distributed.Get(correlationId)
            ?? throw new KeyNotFoundException(
                "Không tìm thấy distributed execution.");

        if (execution.WorkspaceId != workspace.CurrentWorkspaceId)
            throw new DeviceContextSyncValidationException(
                "Distributed execution không thuộc workspace hiện tại.");

        var allowedKinds = NormalizeKinds(request.AllowedKinds);
        var maximumCharacters = request.MaximumCharacters
            ?? DefaultMaximumCharacters;

        if (maximumCharacters is < 200 or > HardMaximumCharacters)
            throw new DeviceContextSyncValidationException(
                $"MaximumCharacters phải từ 200 đến {HardMaximumCharacters:N0}.");

        var device = hub.ReconcileCompanionDevices(workspace.CurrentWorkspaceId)
            .FirstOrDefault(x => x.Id == execution.DeviceId)
            ?? throw new DeviceContextSyncValidationException(
                "Thiết bị đích không còn tồn tại.");

        if (device.ConnectionStatus != DeviceHubConnectionStatuses.Online)
            throw new DeviceContextSyncValidationException(
                "Thiết bị đích đang offline; context sync bị chặn.");

        var access = capabilities.CheckAccess(
            workspace.CurrentWorkspaceId,
            execution.DeviceId,
            execution.RequiredCapability);

        if (!access.Allowed)
            throw new DeviceContextSyncValidationException(
                $"Capability permission không còn hợp lệ: {access.Reason}.");

        var items = new List<DeviceContextSyncItem>();
        var remaining = maximumCharacters;

        if (allowedKinds.Contains(DeviceContextKinds.Memory) && remaining > 0)
        {
            var messages = new[] { new ChatMessage("user", query) };
            var grounded = await memoryGrounding.GroundAsync(
                messages,
                messages,
                cancellationToken);

            foreach (var memory in grounded.Memories)
            {
                if (memory.Content.Length > Math.Min(remaining, MaximumMemoryCharacters))
                    continue;

                items.Add(new(
                    DeviceContextKinds.Memory,
                    memory.Id.ToString("D"),
                    memory.Kind,
                    memory.Content,
                    memory.Content.Length));
                remaining -= memory.Content.Length;
                if (remaining <= 0) break;
            }
        }

        if (allowedKinds.Contains(DeviceContextKinds.Task) && remaining > 0)
        {
            var task = tasks.Get(execution.TaskId);
            if (task is not null)
            {
                var taskText = BuildTaskContext(task);
                if (taskText.Length <= Math.Min(remaining, MaximumTaskCharacters))
                {
                    items.Add(new(
                        DeviceContextKinds.Task,
                        task.Id.ToString("D"),
                        task.Goal,
                        taskText,
                        taskText.Length));
                    remaining -= taskText.Length;
                }
            }
        }

        if (allowedKinds.Contains(DeviceContextKinds.LifeContext) && remaining > 0)
        {
            var selections = await lifeContext.SelectRelevantAsync(
                query,
                Math.Min(remaining, MaximumLifeContextCharacters),
                MaximumLifeContextEntries,
                cancellationToken);

            foreach (var selection in selections)
            {
                var entry = selection.Entry;
                if (entry.Content.Length > remaining)
                    continue;

                items.Add(new(
                    DeviceContextKinds.LifeContext,
                    entry.Id.ToString("D"),
                    entry.SourceName,
                    entry.Content,
                    entry.Content.Length));
                remaining -= entry.Content.Length;
                if (remaining <= 0) break;
            }
        }

        var package = new DeviceContextSyncPackage(
            Guid.NewGuid(),
            correlationId,
            workspace.CurrentWorkspaceId,
            execution.DeviceId,
            query,
            allowedKinds.Order(StringComparer.Ordinal).ToArray(),
            maximumCharacters,
            items.Sum(x => x.CharacterCount),
            FullMemorySync: false,
            DateTimeOffset.UtcNow,
            items);

        lock (_gate)
            _packages.Add(package);

        audit.Record(
            AuditAgents.System,
            "device-context.sync",
            $"context-sync:{package.SyncId:D}",
            $"correlation:{correlationId:D};device:{execution.DeviceId:D};items:{items.Count};characters:{package.UsedCharacters};full-memory-sync:false",
            AuditResults.Succeeded,
            workspaceId: workspace.CurrentWorkspaceId);

        return package;
    }

    private static IReadOnlySet<string> NormalizeKinds(
        IReadOnlyList<string>? kinds)
    {
        if (kinds is null || kinds.Count == 0)
            return new HashSet<string>(
                DeviceContextKinds.All,
                StringComparer.Ordinal);

        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in kinds)
        {
            var value = (kind ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            if (!DeviceContextKinds.All.Contains(value))
                throw new DeviceContextSyncValidationException(
                    "AllowedKinds chỉ hỗ trợ memory, task hoặc life-context.");

            normalized.Add(value);
        }

        return normalized;
    }

    private static string BuildTaskContext(PersonalTask task)
    {
        var steps = string.Join(
            "\n",
            task.Steps.Select(step =>
                $"- [{step.Status}] {step.Title}: {step.Description}"));

        return $"""
        Goal: {task.Goal}
        Status: {task.Status}
        Current step: {task.CurrentStep}
        Plan: {task.Plan}
        Steps:
        {steps}
        """.Trim();
    }
}
