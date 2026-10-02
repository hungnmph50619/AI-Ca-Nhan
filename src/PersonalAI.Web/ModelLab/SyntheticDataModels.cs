using System.Text.Json;

namespace PersonalAI.Web.ModelLab;

public sealed record GenerateSyntheticDataRequest(
    string DatasetId,
    int SourceVersion,
    string Instruction,
    int Count = 10,
    bool ConfirmExternalAi = false);

public sealed record CommitSyntheticDataRequest(
    Guid DraftId,
    bool ConfirmCreateDatasetVersion = false,
    int? ExpectedLatestVersion = null,
    string? VersionNote = null);

public sealed record SyntheticDataIssue(
    int? Index,
    string Code,
    string Message);

public sealed record SyntheticDataDraft(
    Guid Id,
    string WorkspaceId,
    string DatasetId,
    int SourceVersion,
    string SourceSha256,
    string Provider,
    string Model,
    string PromptSha256,
    string Instruction,
    int RequestedCount,
    IReadOnlyList<JsonElement> Items,
    int AcceptedCount,
    int RejectedCount,
    IReadOnlyList<SyntheticDataIssue> Issues,
    bool ReadyToCommit,
    DateTimeOffset CreatedAt);

public sealed record SyntheticDataCommitResult(
    string Version,
    string WorkspaceId,
    Guid DraftId,
    string DatasetId,
    int SourceVersion,
    int CreatedVersion,
    int SourceItems,
    int SyntheticItems,
    int TotalItems,
    string ContentSha256,
    DateTimeOffset CreatedAt);

public sealed record SyntheticDataStatus(
    string Version,
    string WorkspaceId,
    int Drafts,
    int ReadyDrafts,
    int MaximumItemsPerDraft,
    bool ExternalAiRequiresConfirmation,
    bool CommitRequiresConfirmation,
    bool SourceVersionImmutable,
    bool CriticPassRequiredForCommit,
    bool VerificationRequiredForCommit);

public sealed class SyntheticDataValidationException(string message)
    : Exception(message);
