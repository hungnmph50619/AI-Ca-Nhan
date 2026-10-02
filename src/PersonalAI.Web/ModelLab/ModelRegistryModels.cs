namespace PersonalAI.Web.ModelLab;

public static class ModelDeploymentStages
{
    public const string Candidate = "candidate";
    public const string Staging = "staging";
    public const string Canary = "canary";
    public const string Production = "production";
    public const string Deprecated = "deprecated";
    public const string Rejected = "rejected";
    public const string Archived = "archived";

    public static bool IsValid(string value) =>
        value is Candidate or Staging or Canary or Production or
            Deprecated or Rejected or Archived;
}

public sealed record RegisterModelVersionRequest(
    string Family,
    string Version,
    Guid ArtifactId,
    IReadOnlyList<string>? CompatibleTasks = null,
    string? Runtime = null,
    string? Notes = null);

public sealed record UpdateModelDeploymentStageRequest(
    string Stage,
    string? Reason = null);

public sealed record ModelVersionRecord(
    Guid Id,
    string WorkspaceId,
    string Family,
    string Version,
    Guid ArtifactId,
    string ArtifactSha256,
    string BaseModel,
    string TrainingMethod,
    string DatasetId,
    int DatasetVersion,
    string DatasetSha256,
    IReadOnlyList<string> CompatibleTasks,
    string Runtime,
    string Stage,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ModelFamilySummary(
    string Family,
    int Versions,
    string? ProductionVersion,
    string? CanaryVersion,
    string LatestVersion,
    DateTimeOffset UpdatedAt);

public sealed record ModelRegistryStatus(
    string Version,
    string WorkspaceId,
    int Families,
    int Versions,
    int Candidates,
    int Staging,
    int Canary,
    int Production,
    int Deprecated,
    int Rejected,
    int Archived,
    bool Persistent,
    bool SingleProductionPerFamily);

public sealed class ModelRegistryValidationException(string message)
    : Exception(message);

public sealed class ModelRegistryConflictException(string message)
    : Exception(message);
