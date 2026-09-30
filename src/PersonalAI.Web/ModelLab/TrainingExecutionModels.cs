using System.Text.Json;

namespace PersonalAI.Web.ModelLab;

public sealed record QueueTrainingJobRequest(
    string ProviderId = "mock",
    int? TimeoutSeconds = null);

public sealed record TrainingExecutionRecord(
    Guid TrainingJobId,
    string WorkspaceId,
    string ProviderId,
    string? ExternalJobId,
    string Status,
    int Attempt,
    int TimeoutSeconds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason,
    JsonElement? Metrics);

public sealed record TrainingExecutorStatus(
    string Version,
    int Queued,
    int Running,
    int Failed,
    int Completed,
    int Cancelled,
    int MaximumConcurrentJobs,
    int DefaultTimeoutSeconds,
    bool PersistentQueue,
    bool CrashRecoveryEnabled);

public static class TrainingExecutionStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Interrupted = "interrupted";

    public static bool IsTerminal(string status) =>
        status is Completed or Failed or Cancelled;
}

public sealed class TrainingExecutionValidationException(string message)
    : Exception(message);
