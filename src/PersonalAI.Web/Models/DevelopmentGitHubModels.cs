namespace PersonalAI.Web.Models;

public sealed record RunDevelopmentGitHubRequest(
    Guid DevelopmentRunId,
    string Repository,
    string BaseBranch,
    string Title,
    string Body,
    string CredentialRef,
    string Remote = "origin",
    bool ConfirmPush = false,
    bool ConfirmCreatePullRequest = false);

public sealed record DevelopmentGitHubReport(
    Guid Id,
    string WorkspaceId,
    Guid DevelopmentRunId,
    string Repository,
    string Branch,
    string BaseBranch,
    string WorktreePath,
    string Remote,
    bool Pushed,
    bool PullRequestCreated,
    long PullRequestNumber,
    string PullRequestUrl,
    Guid? CiRunId,
    string? CiState,
    string? CiConclusion,
    int CiRepairAttempts,
    int MaximumCiRepairAttempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DevelopmentGitHubStatus(
    string Version,
    string WorkspaceId,
    int Reports,
    bool DirectMainPushAllowed,
    bool PullRequestRequired,
    int MaximumCiRepairAttempts,
    bool CiFailureExcerptReadable,
    bool FullGitHubLogDownloadEnabled);

public sealed record DevelopmentGitHubCiRefreshResult(
    DevelopmentGitHubReport Report,
    string? FailureLogExcerpt,
    bool FailureLogTruncated);

public sealed record DevelopmentGitHubCiRepairRequest(
    string Reason);

public sealed class DevelopmentGitHubValidationException(string message)
    : Exception(message);
