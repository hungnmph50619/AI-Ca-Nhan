namespace PersonalAI.Web.Models;

public static class DevelopmentLocalSyncStates
{
    public const string Synced = "synced";
    public const string DeferredDirtyTree = "deferred-dirty-tree";
    public const string Failed = "failed";
}

public sealed record RunDevelopmentLocalSyncRequest(
    Guid DevelopmentRunId,
    string BaseBranch,
    string Remote = "origin",
    string? CredentialRef = null,
    bool ConfirmSync = false);

public sealed record DevelopmentLocalSyncReport(
    Guid Id,
    string WorkspaceId,
    Guid DevelopmentRunId,
    string RepositoryPath,
    string Remote,
    string BaseBranch,
    string? BranchBefore,
    string? BranchAfter,
    string State,
    bool FetchSucceeded,
    bool DirtyTreeDetected,
    bool CheckoutPerformed,
    bool PullFastForwardOnly,
    bool Synced,
    string? DeferredReason,
    DateTimeOffset CreatedAt);

public sealed record DevelopmentLocalSyncStatus(
    string Version,
    string WorkspaceId,
    int Reports,
    int SyncedReports,
    int DeferredReports,
    bool DirtyTreeDefersSync,
    bool PullFastForwardOnly,
    bool ResetEnabled,
    bool CleanEnabled);

public sealed class DevelopmentLocalSyncValidationException(string message)
    : Exception(message);
