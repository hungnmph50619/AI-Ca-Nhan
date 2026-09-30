namespace PersonalAI.Web.ModelLab;

public sealed record CleanModelLabDatasetRequest(
    string DatasetId,
    int SourceVersion,
    bool CreateCleanedVersion = false,
    bool ConfirmCreateCleanedVersion = false,
    int? ExpectedLatestVersion = null,
    string? VersionNote = null);

public sealed record DataCleaningRemoval(
    int SourceIndex,
    string Reason,
    string Category,
    string Fingerprint);

public sealed record DataCleaningReport(
    string Version,
    string WorkspaceId,
    string DatasetId,
    int SourceVersion,
    int SourceItems,
    int KeptItems,
    int RemovedItems,
    int DuplicateItems,
    int BadExampleItems,
    int SecretItems,
    int CorruptItems,
    IReadOnlyList<DataCleaningRemoval> Removals,
    bool PreviewOnly,
    int? CreatedVersion,
    string? CreatedContentSha256,
    DateTimeOffset CompletedAt);

public static class DataCleaningCategories
{
    public const string Duplicate = "duplicate";
    public const string BadExample = "bad-example";
    public const string Secret = "secret";
    public const string Corrupt = "corrupt";
}

public sealed class DataCleaningValidationException(string message)
    : Exception(message);
