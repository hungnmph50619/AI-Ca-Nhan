namespace PersonalAI.Web.SelfImprovement;

public sealed record SelfCodingFileEdit(
    string Path,
    string Content,
    string Mode,
    string? ExpectedSha256 = null);

public sealed record RunSelfCodingRequest(
    AutomatedExperimentPlan Experiment,
    ImprovementProposal Proposal,
    IReadOnlyList<SelfCodingFileEdit> Edits,
    bool ConfirmBranchCreation,
    bool ConfirmFileChanges,
    Guid? DevelopmentRunId = null);

public sealed record SelfCodingFileResult(
    string Path,
    string Mode,
    bool Created,
    string Sha256);

public sealed record SelfCodingResult(
    string Version,
    string WorkspaceId,
    string ExperimentId,
    string Branch,
    bool BranchCreated,
    int FilesChanged,
    IReadOnlyList<SelfCodingFileResult> Files,
    bool CommitCreated,
    bool Pushed,
    bool Merged,
    DateTimeOffset CompletedAt,
    Guid? DevelopmentRunId = null,
    string? WorktreePath = null,
    bool MainWorktreeModified = false,
    bool AuditPerEdit = true);

public sealed class SelfCodingValidationException(string message)
    : Exception(message);
