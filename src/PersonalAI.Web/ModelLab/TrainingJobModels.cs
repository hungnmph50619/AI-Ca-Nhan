using System.Text.Json;

namespace PersonalAI.Web.ModelLab;

public static class TrainingJobStatuses
{
    public const string Pending = "pending";
    public const string Validating = "validating";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Evaluating = "evaluating";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    public static bool IsTerminal(string status) =>
        status is Completed or Failed or Cancelled;
}

public sealed record TrainingJob(
    Guid Id,
    string WorkspaceId,
    string DatasetId,
    int DatasetVersion,
    string DatasetSha256,
    string BaseModel,
    string TrainingMethod,
    JsonElement Hyperparameters,
    int? Seed,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ArtifactPath,
    JsonElement? Metrics,
    string? FailureReason,
    string ConfigSha256);

public sealed record CreateTrainingJobRequest(
    string DatasetId,
    int DatasetVersion,
    string BaseModel,
    string TrainingMethod,
    JsonElement? Hyperparameters = null,
    int? Seed = null,
    string? ExpectedDatasetSha256 = null);

public sealed record TrainingJobStatus(
    string Version,
    string Storage,
    string WorkspaceId,
    int Jobs,
    int ActiveJobs,
    int MaximumJobsPerWorkspace,
    bool Persistent,
    bool ConfigImmutable,
    bool TrainingExecutionEnabled);

public sealed class TrainingJobValidationException(string message)
    : Exception(message);

public sealed class TrainingJobConflictException(string message)
    : Exception(message);
