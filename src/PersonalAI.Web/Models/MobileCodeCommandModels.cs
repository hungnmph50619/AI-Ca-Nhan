namespace PersonalAI.Web.Models;

public static class MobileCodeCommandStatuses
{
    public const string PendingDesktopApproval = "pending-desktop-approval";
    public const string Running = "running";
    public const string AwaitingUserConfirmation = "awaiting-user-confirmation";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public sealed record SubmitMobileCodeCommandRequest(
    string Goal,
    string? RepositoryHint = null,
    bool ConfirmRequest = false);

public sealed record ExecuteMobileCodeCommandRequest(
    Guid TargetDeviceId,
    string RepositoryPath,
    string BaseBranch,
    string GitHubRepository,
    string CredentialRef,
    bool ConfirmDesktopApproval = false,
    bool ConfirmExternalAi = false,
    bool ConfirmGitHubSideEffects = false);

public sealed record MobileCodeCommand(
    Guid Id,
    string WorkspaceId,
    Guid CompanionDeviceId,
    string CompanionDeviceName,
    string Goal,
    string? RepositoryHint,
    string Status,
    Guid? TargetDeviceId,
    string? RepositoryPath,
    string? BaseBranch,
    string? GitHubRepository,
    string? ExperimentBranch,
    Guid? DevelopmentRunId,
    long? PullRequestNumber,
    string? PullRequestUrl,
    string? StopReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record MobileCodeCommandStatus(
    string Version,
    string WorkspaceId,
    bool CompanionRequestEnabled,
    bool DirectRemoteShellEnabled,
    bool DirectMainPushAllowed,
    bool DesktopApprovalRequired,
    bool GitHubCredentialAcceptedFromPhone,
    bool PullRequestRequired,
    bool TestGateRequired,
    int MaximumGoalCharacters);

public sealed class MobileCodeCommandValidationException(string message)
    : Exception(message);
