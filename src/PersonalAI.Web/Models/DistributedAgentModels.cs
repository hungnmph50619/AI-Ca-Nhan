namespace PersonalAI.Web.Models;

public static class DistributedAgentStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Blocked = "blocked";
}

public sealed record DispatchDistributedAgentRequest(
    string AgentId,
    string Goal,
    string RequiredCapability,
    bool ConfirmDispatch = false);

public sealed record CompleteDistributedAgentRequest(
    Guid CorrelationId,
    bool Succeeded,
    string? Message = null,
    string? Error = null);

public sealed record DistributedPermissionSnapshot(
    string Capability,
    Guid RegistrationId,
    bool Registered,
    bool PermissionGranted,
    DateTimeOffset? PermissionUpdatedAt);

public sealed record DistributedAgentExecution(
    Guid Id,
    Guid CorrelationId,
    string WorkspaceId,
    string AgentId,
    string Goal,
    string RequiredCapability,
    Guid TargetDeviceId,
    string TargetDeviceName,
    string TargetDeviceType,
    DistributedPermissionSnapshot PermissionSnapshot,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? CompletedAt,
    string? Message,
    string? Error);

public sealed record DistributedAgentEnvelope(
    Guid ExecutionId,
    Guid CorrelationId,
    string AgentId,
    string Goal,
    string RequiredCapability,
    DistributedPermissionSnapshot PermissionSnapshot,
    DateTimeOffset CreatedAt);

public sealed record DistributedAgentStatus(
    string Version,
    string WorkspaceId,
    int Executions,
    int Queued,
    int Running,
    int Completed,
    bool CorrelationIdRequired,
    bool PermissionRecheckedOnClaim,
    bool PermissionRecheckedOnComplete,
    bool UntrustedNodesEligible,
    bool OfflineNodesEligible,
    bool CompanionTransportEnabled);

public sealed class DistributedAgentValidationException(string message)
    : Exception(message);
