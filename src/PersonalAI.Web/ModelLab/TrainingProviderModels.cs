using System.Text.Json;

namespace PersonalAI.Web.ModelLab;

public sealed record TrainingProviderCapabilities(
    string ProviderId,
    string DisplayName,
    bool SupportsFullFineTune,
    bool SupportsLoRa,
    bool SupportsQLoRa,
    bool SupportsResume,
    bool SupportsMetrics,
    bool SupportsCancellation,
    bool SupportsGpu,
    bool ExternalService,
    IReadOnlyList<string> SupportedMethods);

public sealed record TrainingProviderValidationRequest(
    string ProviderId,
    string BaseModel,
    string TrainingMethod,
    JsonElement? Hyperparameters = null);

public sealed record TrainingProviderValidationResult(
    bool Valid,
    string ProviderId,
    string BaseModel,
    string TrainingMethod,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record TrainingProviderStartRequest(
    Guid TrainingJobId,
    string BaseModel,
    string TrainingMethod,
    JsonElement Hyperparameters,
    int? Seed);

public sealed record TrainingProviderExecutionHandle(
    string ProviderId,
    string ExternalJobId,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record TrainingProviderProgress(
    string ProviderId,
    string ExternalJobId,
    string Status,
    double? ProgressPercent,
    JsonElement? Metrics,
    string? FailureReason,
    DateTimeOffset CheckedAt);

public static class TrainingProviderExecutionStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public sealed class TrainingProviderValidationException(string message)
    : Exception(message);
