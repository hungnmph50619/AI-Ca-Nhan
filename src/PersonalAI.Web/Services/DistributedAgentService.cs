using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDistributedAgentService
{
    DistributedAgentStatus GetStatus();
    IReadOnlyList<DistributedAgentExecution> GetAll();
    DistributedAgentExecution? Get(Guid executionId);
    DistributedAgentExecution Dispatch(DispatchDistributedAgentRequest request);
    DistributedAgentEnvelope? ClaimNext(CompanionDevice companionDevice);
    DistributedAgentExecution Complete(
        CompanionDevice companionDevice,
        Guid executionId,
        CompleteDistributedAgentRequest request);
}

public sealed class DistributedAgentService(
    IAgentRegistry agents,
    IDeviceHubService hub,
    IDeviceIdentityService identities,
    IDeviceCapabilityService capabilities,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IDistributedAgentService
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);

    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public DistributedAgentStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.Status == DistributedAgentStatuses.Queued),
            all.Count(x => x.Status == DistributedAgentStatuses.Running),
            all.Count(x => x.Status == DistributedAgentStatuses.Completed),
            CorrelationIdRequired: true,
            PermissionRecheckedOnClaim: true,
            PermissionRecheckedOnComplete: true,
            UntrustedNodesEligible: false,
            OfflineNodesEligible: false,
            CompanionTransportEnabled: true);
    }

    public IReadOnlyList<DistributedAgentExecution> GetAll()
    {
        lock (_gate)
            return Load()
                .OrderByDescending(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .ToArray();
    }

    public DistributedAgentExecution? Get(Guid executionId)
    {
        lock (_gate)
            return Load().FirstOrDefault(x => x.Id == executionId);
    }

    public DistributedAgentExecution Dispatch(
        DispatchDistributedAgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmDispatch)
            throw new DistributedAgentValidationException(
                "Cần ConfirmDispatch=true để tạo distributed execution.");

        var agentId = (request.AgentId ?? string.Empty).Trim();
        if (!agents.TryGet(agentId, out var agent) || agent is null)
            throw new DistributedAgentValidationException(
                "Agent chưa được đăng ký trong Agent Framework.");

        var goal = (request.Goal ?? string.Empty).Trim();
        if (goal.Length is < 1 or > AgentFrameworkLimits.MaximumGoalCharacters)
            throw new DistributedAgentValidationException(
                "Goal không hợp lệ hoặc vượt giới hạn Agent Framework.");

        var capability = NormalizeCapability(request.RequiredCapability);
        var devices = hub.ReconcileCompanionDevices(
            workspace.CurrentWorkspaceId);

        var target = devices
            .Where(x =>
                x.Source == DeviceHubSources.Companion &&
                x.ConnectionStatus == DeviceHubConnectionStatuses.Online)
            .Select(x => new
            {
                Device = x,
                Identity = identities.Get(x.WorkspaceId, x.Id),
                Access = capabilities.CheckAccess(
                    x.WorkspaceId,
                    x.Id,
                    capability),
                Registration = capabilities
                    .GetAll(x.WorkspaceId, x.Id)
                    .FirstOrDefault(r =>
                        r.Capability == capability &&
                        r.Registered)
            })
            .Where(x =>
                x.Identity?.TrustLevel == DeviceTrustLevels.Trusted &&
                x.Access.Allowed &&
                x.Registration is not null)
            .OrderBy(x => DeviceTypeRank(x.Device.DeviceType))
            .ThenBy(x => x.Device.Id.ToString("N"), StringComparer.Ordinal)
            .FirstOrDefault();

        if (target is null || target.Registration is null)
            throw new DistributedAgentValidationException(
                "Không có trusted online node có capability permission phù hợp.");

        var snapshot = new DistributedPermissionSnapshot(
            capability,
            target.Registration.Id,
            Registered: true,
            PermissionGranted: true,
            target.Registration.PermissionUpdatedAt);

        var now = DateTimeOffset.UtcNow;
        var execution = new DistributedAgentExecution(
            Guid.NewGuid(),
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            agent.Definition.Id,
            goal,
            capability,
            target.Device.Id,
            target.Device.Name,
            target.Device.DeviceType,
            snapshot,
            DistributedAgentStatuses.Queued,
            now,
            ClaimedAt: null,
            CompletedAt: null,
            Message: null,
            Error: null);

        lock (_gate)
        {
            var all = Load();
            all.Add(execution);
            Save(all);
        }

        audit.Record(
            AuditAgents.System,
            "agent.distributed.dispatch",
            $"agent-execution:{execution.Id:D}",
            $"correlation:{execution.CorrelationId:D};agent:{execution.AgentId};device:{execution.TargetDeviceId:D};capability:{capability};permission:true",
            AuditResults.Prepared,
            workspaceId: execution.WorkspaceId);

        return execution;
    }

    public DistributedAgentEnvelope? ClaimNext(
        CompanionDevice companionDevice)
    {
        ArgumentNullException.ThrowIfNull(companionDevice);
        var hubDevice = hub.RecordCompanionHeartbeat(companionDevice);
        var identity = identities.Get(
            hubDevice.WorkspaceId,
            hubDevice.Id);

        if (identity?.TrustLevel != DeviceTrustLevels.Trusted)
            throw new DistributedAgentValidationException(
                "Node phải secure-paired và trusted.");

        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x =>
                x.TargetDeviceId == hubDevice.Id &&
                x.Status == DistributedAgentStatuses.Queued);

            if (index < 0)
                return null;

            var current = all[index];
            var access = capabilities.CheckAccess(
                hubDevice.WorkspaceId,
                hubDevice.Id,
                current.RequiredCapability);

            if (!access.Allowed)
            {
                all[index] = current with
                {
                    Status = DistributedAgentStatuses.Blocked,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Error = "permission-revoked-before-claim"
                };
                Save(all);

                throw new DistributedAgentValidationException(
                    "Capability permission không còn hợp lệ; execution đã bị chặn.");
            }

            var running = current with
            {
                Status = DistributedAgentStatuses.Running,
                ClaimedAt = DateTimeOffset.UtcNow
            };
            all[index] = running;
            Save(all);

            audit.Record(
                AuditAgents.Companion,
                "agent.distributed.claim",
                $"agent-execution:{running.Id:D}",
                $"correlation:{running.CorrelationId:D};device:{hubDevice.Id:D};capability:{running.RequiredCapability};permission-rechecked:true",
                AuditResults.Prepared,
                workspaceId: running.WorkspaceId);

            return new(
                running.Id,
                running.CorrelationId,
                running.AgentId,
                running.Goal,
                running.RequiredCapability,
                running.PermissionSnapshot,
                running.CreatedAt);
        }
    }

    public DistributedAgentExecution Complete(
        CompanionDevice companionDevice,
        Guid executionId,
        CompleteDistributedAgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(companionDevice);
        ArgumentNullException.ThrowIfNull(request);

        var hubDevice = hub.RecordCompanionHeartbeat(companionDevice);

        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x => x.Id == executionId);
            if (index < 0)
                throw new KeyNotFoundException(
                    "Không tìm thấy distributed execution.");

            var current = all[index];
            if (current.TargetDeviceId != hubDevice.Id)
                throw new DistributedAgentValidationException(
                    "Execution không thuộc thiết bị hiện tại.");

            if (current.CorrelationId != request.CorrelationId)
                throw new DistributedAgentValidationException(
                    "CorrelationId không khớp.");

            if (current.Status != DistributedAgentStatuses.Running)
                throw new DistributedAgentValidationException(
                    "Execution không ở trạng thái running.");

            var identity = identities.Get(
                hubDevice.WorkspaceId,
                hubDevice.Id);
            var access = capabilities.CheckAccess(
                hubDevice.WorkspaceId,
                hubDevice.Id,
                current.RequiredCapability);

            if (identity?.TrustLevel != DeviceTrustLevels.Trusted ||
                !access.Allowed)
            {
                var blocked = current with
                {
                    Status = DistributedAgentStatuses.Blocked,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Error = "permission-or-trust-revoked-before-complete"
                };
                all[index] = blocked;
                Save(all);

                throw new DistributedAgentValidationException(
                    "Trust hoặc capability permission đã bị thu hồi; kết quả không được chấp nhận.");
            }

            var completed = current with
            {
                Status = request.Succeeded
                    ? DistributedAgentStatuses.Completed
                    : DistributedAgentStatuses.Failed,
                CompletedAt = DateTimeOffset.UtcNow,
                Message = Limit(request.Message, 4000),
                Error = request.Succeeded
                    ? null
                    : Limit(request.Error, 2000)
            };

            all[index] = completed;
            Save(all);

            audit.Record(
                AuditAgents.Companion,
                "agent.distributed.complete",
                $"agent-execution:{completed.Id:D}",
                $"correlation:{completed.CorrelationId:D};device:{hubDevice.Id:D};capability:{completed.RequiredCapability};permission-rechecked:true;status:{completed.Status}",
                request.Succeeded
                    ? AuditResults.Succeeded
                    : AuditResults.Failed,
                workspaceId: completed.WorkspaceId);

            return completed;
        }
    }

    private List<DistributedAgentExecution> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<DistributedAgentExecution>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            throw new DistributedAgentValidationException(
                "Distributed agent state bị hỏng; từ chối execution.");
        }
    }

    private void Save(List<DistributedAgentExecution> executions)
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
                char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

        return Path.Combine(
            _root,
            $"distributed-agent-{safe}.json");
    }

    private static string NormalizeCapability(string? value)
    {
        var capability = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (!DeviceCapabilityNames.All.Contains(capability))
            throw new DistributedAgentValidationException(
                "RequiredCapability không hợp lệ.");

        return capability;
    }

    private static int DeviceTypeRank(string type) =>
        type switch
        {
            DeviceHubDeviceTypes.Pc => 0,
            DeviceHubDeviceTypes.Laptop => 1,
            DeviceHubDeviceTypes.Android => 2,
            _ => 3
        };

    private static string? Limit(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();
        return text.Length <= maximum
            ? text
            : text[..maximum];
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["DistributedAgent:Root"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "DistributedAgent");
        }

        root = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }
}
