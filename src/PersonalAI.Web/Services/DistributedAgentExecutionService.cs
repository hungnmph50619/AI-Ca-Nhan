using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDistributedAgentExecutionService
{
    DistributedAgentExecutionStatus GetStatus();
    IReadOnlyList<DistributedAgentExecution> GetAll();
    DistributedAgentExecution? Get(Guid correlationId);
    DistributedAgentExecution Dispatch(
        Guid taskId,
        DispatchDistributedAgentRequest request);
    DistributedAgentExecution Claim(
        Guid correlationId,
        Guid deviceId,
        ClaimDistributedAgentRequest request);
    DistributedAgentExecution Complete(
        Guid correlationId,
        Guid deviceId,
        CompleteDistributedAgentRequest request);
}

public sealed class DistributedAgentExecutionService(
    ITaskEngineService tasks,
    ITaskRouterService router,
    IAgentRegistry agents,
    IDeviceHubService hub,
    IDeviceIdentityService identities,
    IDeviceCapabilityService capabilities,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IDistributedAgentExecutionService
{
    public const int MaximumContextCharacters = 8_000;
    public const int MaximumResultCharacters = 8_000;

    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);

    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public DistributedAgentExecutionStatus GetStatus() =>
        new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            CorrelationIdRequired: true,
            RouteRequiredBeforeDispatch: true,
            PermissionRevalidatedOnDispatch: true,
            PermissionRevalidatedOnClaim: true,
            PermissionRevalidatedOnComplete: true,
            OfflineDeviceExecutionBlocked: true,
            CompanionTrustRevalidated: true,
            ExecutionStatePersisted: true,
            MaximumContextCharacters,
            MaximumResultCharacters,
            DeviceCapabilityNames.All
                .Order(StringComparer.Ordinal)
                .ToArray());

    public IReadOnlyList<DistributedAgentExecution> GetAll()
    {
        lock (_gate)
            return Load()
                .OrderByDescending(x => x.CreatedAt)
                .ToArray();
    }

    public DistributedAgentExecution? Get(Guid correlationId)
    {
        lock (_gate)
            return Load()
                .FirstOrDefault(x =>
                    x.CorrelationId == correlationId);
    }

    public DistributedAgentExecution Dispatch(
        Guid taskId,
        DispatchDistributedAgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmDispatch)
            throw new DistributedAgentExecutionValidationException(
                "Cần ConfirmDispatch=true trước khi chuyển agent context sang thiết bị đã route.");

        var task = tasks.Get(taskId)
            ?? throw new KeyNotFoundException(
                "Không tìm thấy tác vụ.");

        if (task.WorkspaceId != workspace.CurrentWorkspaceId)
            throw new DistributedAgentExecutionValidationException(
                "Tác vụ không thuộc workspace hiện tại.");

        if (task.Status is PersonalTaskStatuses.Completed
            or PersonalTaskStatuses.Cancelled
            or PersonalTaskStatuses.Failed)
        {
            throw new DistributedAgentExecutionValidationException(
                "Không dispatch agent cho tác vụ đã kết thúc.");
        }

        var agentId = NormalizeAgentId(request.AgentId);
        if (!agents.TryGet(agentId, out var agent)
            || agent is null)
        {
            throw new DistributedAgentExecutionValidationException(
                "AgentId chưa được đăng ký trong Agent Framework.");
        }

        var capability = NormalizeCapability(
            request.RequiredCapability);
        var context = NormalizePayload(
            request.Context,
            MaximumContextCharacters,
            "Context");

        var route = router.GetLatest(taskId)
            ?? throw new DistributedAgentExecutionValidationException(
                "Tác vụ phải được route trước khi distributed execution.");

        if (route.Decision != "routed"
            || route.DeviceId is null)
        {
            throw new DistributedAgentExecutionValidationException(
                "Route hiện tại không có thiết bị hợp lệ để dispatch.");
        }

        if (!route.RequiredCapability.Equals(
                capability,
                StringComparison.Ordinal))
        {
            throw new DistributedAgentExecutionValidationException(
                "RequiredCapability phải khớp capability của route hiện tại.");
        }

        var device = RequireExecutionAccess(
            route.DeviceId.Value,
            capability);

        var execution = new DistributedAgentExecution(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            task.Id,
            agent.Definition.Id,
            capability,
            context,
            device.Id,
            device.Name,
            device.DeviceType,
            DistributedAgentExecutionStatuses.Queued,
            DateTimeOffset.UtcNow,
            ClaimedAt: null,
            CompletedAt: null,
            ResultSummary: null);

        lock (_gate)
        {
            var all = Load();
            all.Add(execution);
            Save(all);
        }

        audit.Record(
            agent.Definition.Id,
            "agent.distributed.dispatch",
            $"correlation:{execution.CorrelationId:D}",
            $"task:{task.Id:D};device:{device.Id:D};capability:{capability};permission-revalidated:true",
            AuditResults.Prepared,
            workspaceId: workspace.CurrentWorkspaceId);

        return execution;
    }

    public DistributedAgentExecution Claim(
        Guid correlationId,
        Guid deviceId,
        ClaimDistributedAgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmClaim)
            throw new DistributedAgentExecutionValidationException(
                "Cần ConfirmClaim=true để node nhận distributed execution.");

        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x =>
                x.CorrelationId == correlationId);

            if (index < 0)
                throw new KeyNotFoundException(
                    "Không tìm thấy distributed execution.");

            var current = all[index];
            EnsureAssignedDevice(current, deviceId);

            if (current.Status !=
                DistributedAgentExecutionStatuses.Queued)
            {
                throw new DistributedAgentExecutionValidationException(
                    "Execution chỉ có thể claim khi đang queued.");
            }

            _ = RequireExecutionAccess(
                current.DeviceId,
                current.RequiredCapability);

            var updated = current with
            {
                Status =
                    DistributedAgentExecutionStatuses.Claimed,
                ClaimedAt = DateTimeOffset.UtcNow
            };

            all[index] = updated;
            Save(all);

            audit.Record(
                current.AgentId,
                "agent.distributed.claim",
                $"correlation:{correlationId:D}",
                $"device:{deviceId:D};capability:{current.RequiredCapability};permission-revalidated:true",
                AuditResults.Prepared,
                workspaceId: workspace.CurrentWorkspaceId);

            return updated;
        }
    }

    public DistributedAgentExecution Complete(
        Guid correlationId,
        Guid deviceId,
        CompleteDistributedAgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmComplete)
            throw new DistributedAgentExecutionValidationException(
                "Cần ConfirmComplete=true để hoàn tất distributed execution.");

        var result = NormalizePayload(
            request.ResultSummary,
            MaximumResultCharacters,
            "ResultSummary");

        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x =>
                x.CorrelationId == correlationId);

            if (index < 0)
                throw new KeyNotFoundException(
                    "Không tìm thấy distributed execution.");

            var current = all[index];
            EnsureAssignedDevice(current, deviceId);

            if (current.Status !=
                DistributedAgentExecutionStatuses.Claimed)
            {
                throw new DistributedAgentExecutionValidationException(
                    "Execution chỉ có thể complete sau khi đã claim.");
            }

            _ = RequireExecutionAccess(
                current.DeviceId,
                current.RequiredCapability);

            var status = request.Succeeded
                ? DistributedAgentExecutionStatuses.Succeeded
                : DistributedAgentExecutionStatuses.Failed;

            var updated = current with
            {
                Status = status,
                CompletedAt = DateTimeOffset.UtcNow,
                ResultSummary = result
            };

            all[index] = updated;
            Save(all);

            audit.Record(
                current.AgentId,
                "agent.distributed.complete",
                $"correlation:{correlationId:D}",
                $"device:{deviceId:D};capability:{current.RequiredCapability};status:{status};permission-revalidated:true",
                request.Succeeded
                    ? AuditResults.Succeeded
                    : AuditResults.Failed,
                workspaceId: workspace.CurrentWorkspaceId);

            return updated;
        }
    }

    private DeviceHubDevice RequireExecutionAccess(
        Guid deviceId,
        string capability)
    {
        var device = hub.ReconcileCompanionDevices(
                workspace.CurrentWorkspaceId)
            .FirstOrDefault(x => x.Id == deviceId)
            ?? throw new DistributedAgentExecutionValidationException(
                "Thiết bị route không còn tồn tại.");

        if (device.ConnectionStatus !=
            DeviceHubConnectionStatuses.Online)
        {
            throw new DistributedAgentExecutionValidationException(
                "Thiết bị route đang offline; distributed execution bị chặn.");
        }

        if (device.Source == DeviceHubSources.Companion)
        {
            var identity = identities.Get(
                workspace.CurrentWorkspaceId,
                device.Id);

            if (identity?.TrustLevel != DeviceTrustLevels.Trusted)
            {
                throw new DistributedAgentExecutionValidationException(
                    "Companion device không còn trusted; distributed execution bị chặn.");
            }
        }

        var access = capabilities.CheckAccess(
            workspace.CurrentWorkspaceId,
            device.Id,
            capability);

        if (!access.Allowed
            || !access.Registered
            || !access.PermissionGranted)
        {
            throw new DistributedAgentExecutionValidationException(
                $"Capability permission không còn hợp lệ: {access.Reason}.");
        }

        return device;
    }

    private static void EnsureAssignedDevice(
        DistributedAgentExecution execution,
        Guid deviceId)
    {
        if (execution.DeviceId != deviceId)
            throw new DistributedAgentExecutionValidationException(
                "Node không khớp thiết bị đã được Task Router chọn.");
    }

    private static string NormalizeAgentId(string? value)
    {
        var agentId = (value ?? string.Empty).Trim();
        if (agentId.Length is < 3 or > 120)
            throw new DistributedAgentExecutionValidationException(
                "AgentId không hợp lệ.");
        return agentId;
    }

    private static string NormalizeCapability(string? value)
    {
        var capability = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (!DeviceCapabilityNames.All.Contains(capability))
            throw new DistributedAgentExecutionValidationException(
                "RequiredCapability phải là git, build, gpu, gps, camera, notification hoặc voice.");

        return capability;
    }

    private static string NormalizePayload(
        string? value,
        int maximum,
        string fieldName)
    {
        var normalized = (value ?? string.Empty).Trim();

        if (normalized.Length == 0)
            throw new DistributedAgentExecutionValidationException(
                $"{fieldName} không được để trống.");

        if (normalized.Length > maximum)
            throw new DistributedAgentExecutionValidationException(
                $"{fieldName} không được dài hơn {maximum:N0} ký tự.");

        return normalized;
    }

    private List<DistributedAgentExecution> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<
                List<DistributedAgentExecution>>(
                    File.ReadAllText(path),
                    Options) ?? [];
        }
        catch (JsonException)
        {
            throw new DistributedAgentExecutionValidationException(
                "Distributed execution state bị hỏng; từ chối suy đoán execution.");
        }
    }

    private void Save(
        List<DistributedAgentExecution> executions)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(executions, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(
            workspace.CurrentWorkspaceId.Select(c =>
                char.IsLetterOrDigit(c) || c is '-' or '_'
                    ? c
                    : '_'));

        return Path.Combine(
            _root,
            $"distributed-agent-executions-{safe}.json");
    }

    private static string ResolveRoot(
        IConfiguration configuration)
    {
        var root =
            configuration["DistributedAgentExecution:Root"];

        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "DistributedAgentExecution");
        }

        root = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }
}
