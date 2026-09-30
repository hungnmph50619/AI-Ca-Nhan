using System.Text.Json;

namespace PersonalAI.Web.ModelLab;

public static class ModelArtifactStatuses
{
    public const string Candidate = "candidate";
    public const string Validated = "validated";
    public const string Rejected = "rejected";
    public const string Archived = "archived";

    public static bool IsValid(string value) =>
        value is Candidate or Validated or Rejected or Archived;
}

public sealed record ModelArtifact(
    Guid Id,
    string WorkspaceId,
    string Name,
    string Version,
    string BaseModel,
    Guid TrainingJobId,
    string DatasetId,
    int DatasetVersion,
    string DatasetSha256,
    string TrainingMethod,
    string ArtifactPath,
    string ArtifactSha256,
    long SizeBytes,
    JsonElement Metrics,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record RegisterModelArtifactRequest(
    string Name,
    string Version,
    string BaseModel,
    Guid TrainingJobId,
    string DatasetId,
    int DatasetVersion,
    string DatasetSha256,
    string TrainingMethod,
    string ArtifactPath,
    JsonElement Metrics);

public sealed record UpdateModelArtifactStatusRequest(
    string Status);

public sealed record ModelArtifactStoreStatus(
    string Version,
    string WorkspaceId,
    int Artifacts,
    int Candidates,
    int Validated,
    int Rejected,
    int Archived,
    bool Persistent,
    bool IntegrityVerifiedOnRegister);

public sealed class ModelArtifactValidationException(string message)
    : Exception(message);
