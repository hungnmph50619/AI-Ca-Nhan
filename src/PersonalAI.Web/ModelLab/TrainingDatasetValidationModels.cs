namespace PersonalAI.Web.ModelLab;

public sealed record ValidateTrainingDatasetRequest(
    string DatasetId,
    int SourceVersion,
    string? ExpectedContentSha256 = null);

public sealed record TrainingDatasetValidationIssue(
    int? SourceIndex,
    string Code,
    string Severity,
    string Message);

public sealed record TrainingDatasetValidationReport(
    string Version,
    string WorkspaceId,
    string DatasetId,
    int SourceVersion,
    string ContentSha256,
    bool ChecksumMatched,
    bool Valid,
    int TotalExamples,
    int AcceptedExamples,
    int RejectedExamples,
    int WarningCount,
    long EstimatedTokens,
    double DuplicateRatio,
    int SecretCount,
    int SchemaErrorCount,
    int BadExampleCount,
    int CorruptCount,
    bool SourceDatasetUnchanged,
    IReadOnlyList<TrainingDatasetValidationIssue> Issues,
    DateTimeOffset CompletedAt);

public static class TrainingDatasetValidationSeverities
{
    public const string Error = "error";
    public const string Warning = "warning";
}

public static class TrainingDatasetValidationCodes
{
    public const string ChecksumMismatch = "checksum-mismatch";
    public const string InvalidTopLevel = "invalid-top-level";
    public const string MissingTrainingContent = "missing-training-content";
    public const string EmptyTrainingContent = "empty-training-content";
    public const string InvalidMessages = "invalid-messages";
    public const string Duplicate = "duplicate";
    public const string Secret = "secret";
    public const string BadExample = "bad-example";
    public const string Corrupt = "corrupt";
    public const string TextOnlyExample = "text-only-example";
    public const string OversizedExample = "oversized-example";
}

public sealed class TrainingDatasetValidationException(string message)
    : Exception(message);
