namespace PersonalAI.Web.Models;

public sealed record LocalGitRepositoryStatus(
    string Version,
    string WorkspaceId,
    bool GitAvailable,
    bool FetchEnabled,
    bool CheckoutEnabled,
    bool PullFastForwardOnly,
    bool CommitEnabled,
    bool PushEnabled,
    bool ForcePushEnabled,
    bool ResetEnabled,
    bool GenericShellEnabled);

public sealed record LocalGitCommandResult(
    string Operation,
    string RepositoryPath,
    int ExitCode,
    bool Succeeded,
    bool TimedOut,
    int DurationMs,
    string Output,
    bool OutputTruncated);

public sealed record LocalGitBranchList(
    string RepositoryPath,
    string CurrentBranch,
    IReadOnlyList<string> LocalBranches,
    IReadOnlyList<string> RemoteBranches);

public sealed record LocalGitFetchRequest(
    string RepositoryPath,
    string Remote = "origin",
    bool ConfirmGitWrite = false,
    string? CredentialRef = null);

public sealed record LocalGitCheckoutRequest(
    string RepositoryPath,
    string Branch,
    bool ConfirmGitWrite = false);

public sealed record LocalGitCreateBranchRequest(
    string RepositoryPath,
    string Branch,
    string? StartPoint = null,
    bool Checkout = true,
    bool ConfirmGitWrite = false);

public sealed record LocalGitDiffRequest(
    string RepositoryPath,
    bool Staged = false);

public sealed record LocalGitLogRequest(
    string RepositoryPath,
    int MaximumEntries = 30);

public sealed record LocalGitPullRequest(
    string RepositoryPath,
    string Remote = "origin",
    string? Branch = null,
    bool ConfirmGitWrite = false,
    string? CredentialRef = null);

public sealed record LocalGitCommitRequest(
    string RepositoryPath,
    string Message,
    IReadOnlyList<string> Paths,
    bool ConfirmGitWrite = false);

public sealed record LocalGitPushRequest(
    string RepositoryPath,
    string Remote = "origin",
    string? Branch = null,
    bool ConfirmGitWrite = false,
    string? CredentialRef = null);

public sealed class LocalGitRepositoryValidationException(string message)
    : Exception(message);
