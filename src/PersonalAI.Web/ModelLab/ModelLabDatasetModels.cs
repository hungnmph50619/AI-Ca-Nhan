using System.Text.Json;

namespace PersonalAI.Web.ModelLab;

public sealed record ModelLabDatasetSummary(
    string Id,
    string Name,
    string Description,
    string WorkspaceId,
    int LatestVersion,
    int LatestItemCount,
    string LatestContentSha256,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ModelLabDatasetVersion(
    string DatasetId,
    int Version,
    IReadOnlyList<JsonElement> Items,
    int ItemCount,
    string ContentSha256,
    string? Note,
    string WorkspaceId,
    DateTimeOffset CreatedAt);

public sealed record CreateModelLabDatasetRequest(
    string Id,
    string Name,
    string? Description,
    IReadOnlyList<JsonElement> Items,
    string? VersionNote = null);

public sealed record CreateModelLabDatasetVersionRequest(
    IReadOnlyList<JsonElement> Items,
    string? Note = null,
    int? ExpectedLatestVersion = null);

public sealed record ModelLabDatasetStatus(
    string Version,
    string Storage,
    string WorkspaceId,
    int Datasets,
    int Versions,
    int MaximumDatasets,
    int MaximumVersionsPerDataset,
    int MaximumItemsPerVersion,
    int MaximumPayloadBytes,
    bool VersionsImmutable);

public sealed class ModelLabDatasetValidationException(string message)
    : Exception(message);

public sealed class ModelLabDatasetConflictException(string message)
    : Exception(message);
