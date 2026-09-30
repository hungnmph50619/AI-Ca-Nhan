using System.Text.Json;

namespace PersonalAI.Web.ModelLab;

public static class CandidateTrainingStatuses
{
    public const string Queued = "queued";
    public const string ArtifactRegistered = "artifact-registered";
    public const string CandidateRegistered = "candidate-registered";
    public const string Failed = "failed";
}

public sealed record StartCandidateTrainingRequest(
    Guid DraftId,
    int DatasetVersion,
    string Family,
    string Version,
    string BaseModel,
    string TrainingMethod = "naive-bayes",
    JsonElement? Hyperparameters = null,
    int? Seed = null,
    IReadOnlyList<string>? CompatibleTasks = null,
    string? Runtime = "local",
    bool ConfirmStartTraining = false);

public sealed record CandidateTrainingPlan(
    Guid Id,
    string WorkspaceId,
    Guid DraftId,
    Guid VerificationReportId,
    string DatasetId,
    int DatasetVersion,
    string DatasetSha256,
    Guid TrainingJobId,
    string Family,
    string Version,
    IReadOnlyList<string> CompatibleTasks,
    string Runtime,
    string Status,
    Guid? ArtifactId,
    Guid? ModelVersionId,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CandidateTrainingStatus(
    string Version,
    string WorkspaceId,
    int Plans,
    int Queued,
    int CandidateRegistered,
    int Failed,
    bool VerifiedDataRequired,
    bool ProductionMutationEnabled,
    bool ExplicitConfirmationRequired);

public sealed class CandidateTrainingValidationException(string message)
    : Exception(message);
