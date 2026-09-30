namespace PersonalAI.Web.ModelLab;

public static class PersonalDatasetSourceKinds
{
    public const string Memory = "memory";
    public const string RegressionCase = "regression-case";
    public const string KnowledgeChunk = "knowledge-chunk";
}

public sealed record PersonalDatasetSourceSelection(
    string Kind,
    string Id,
    int? ChunkIndex = null);

public sealed record BuildPersonalDatasetRequest(
    string DatasetId,
    string? DatasetName,
    string? Description,
    IReadOnlyList<PersonalDatasetSourceSelection> Sources,
    bool CreateDataset,
    int? ExpectedLatestVersion = null,
    string? VersionNote = null,
    bool ConfirmUseSelectedPersonalData = false);

public sealed record PersonalDatasetBuildItem(
    string SourceKind,
    string SourceId,
    int? ChunkIndex,
    object Data);

public sealed record PersonalDatasetBuildResult(
    string Version,
    string WorkspaceId,
    string DatasetId,
    int DatasetVersion,
    int SourceSelections,
    int ItemsCreated,
    IReadOnlyDictionary<string, int> SourceCounts,
    string ContentSha256,
    bool ExplicitSelectionOnly,
    DateTimeOffset CreatedAt);

public sealed class PersonalDatasetBuilderValidationException(string message)
    : Exception(message);
