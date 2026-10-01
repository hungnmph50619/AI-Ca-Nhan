namespace PersonalAI.Web.Models;

public static class CiMonitorStates
{
    public const string Queued = "queued";
    public const string InProgress = "in-progress";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string TimedOut = "timed-out";
}

public sealed record CiRunRecord(
    Guid Id,
    string WorkspaceId,
    string Repository,
    string SourceEventType,
    string DeliveryId,
    string? ExternalId,
    string? Ref,
    string? Sha,
    string State,
    string? Conclusion,
    int RepairAttempts,
    int MaximumRepairAttempts,
    string? FailureLogExcerpt,
    bool FailureLogTruncated,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record CiMonitorStatus(
    string Version,
    string WorkspaceId,
    int Runs,
    int ActiveRuns,
    int FailedRuns,
    int CancelledRuns,
    int TimedOutRuns,
    int MaximumRepairAttempts,
    bool TimeoutPersisted,
    bool CancellationPersisted,
    bool FailureLogsPersisted,
    bool FullGitHubLogDownloadEnabled);

public sealed record RecordCiFailureLogRequest(
    Guid RunId,
    string LogExcerpt);

public sealed record RequestCiRepairRequest(
    Guid RunId,
    string Reason);

public sealed record MarkCiRunTimedOutRequest(
    Guid RunId,
    string Reason);

public sealed record CancelCiRunRequest(
    Guid RunId,
    string Reason);

public sealed record CiRepairDecision(
    Guid RunId,
    int Attempt,
    int MaximumAttempts,
    bool Allowed,
    string Reason,
    DateTimeOffset CreatedAt);

public sealed class CiMonitorValidationException(string message)
    : Exception(message);

public sealed class CiRepairLimitException(string message)
    : Exception(message);
