namespace PersonalAI.Web.Models;

public static class DistributedAgentExecutionStatuses
{
    public const string Queued = "queued";
    public const string Claimed = "claimed";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    public static readonly IReadOnlySet<string> Terminal =
        new HashSet<string>(
            [Succeeded, Failed],
            StringComparer.Ordinal);
}

public sealed record DispatchDistributedAgentRequest(
    string AgentId,
    string RequiredCapability,
    string Context,
    bool ConfirmDispatch = false);

public sealed record ClaimDistributedAgentRequest(
    bool ConfirmClaim = false);

public sealed record CompleteDistributedAgentRequest(
    bool Succeeded,
    string ResultSummary,
    bool ConfirmComplete = false);

public sealed record DistributedAgentExecution(
    Guid CorrelationId,
    string WorkspaceId,
    Guid TaskId,
    string AgentId,
    string RequiredCapability,
    string Context,
    Guid DeviceId,
    string DeviceName,
    string DeviceType,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? CompletedAt,
    string? ResultSummary);

public sealed record DistributedAgentExecutionStatus(
    string Version,
    string WorkspaceId,
    bool CorrelationIdRequired,
    bool RouteRequiredBeforeDispatch,
    bool PermissionRevalidatedOnDispatch,
    bool PermissionRevalidatedOnClaim,
    bool PermissionRevalidatedOnComplete,
    bool OfflineDeviceExecutionBlocked,
    bool CompanionTrustRevalidated,
    bool ExecutionStatePersisted,
    int MaximumContextCharacters,
    int MaximumResultCharacters,
    IReadOnlyList<string> SupportedCapabilities);

public sealed class DistributedAgentExecutionValidationException(string message)
    : Exception(message);
