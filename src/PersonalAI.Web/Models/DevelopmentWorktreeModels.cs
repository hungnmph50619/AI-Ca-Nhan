namespace PersonalAI.Web.Models;

public sealed record DevelopmentWorktreeStatus(
    string Version,
    string WorkspaceId,
    bool WorktreeIsolationEnabled,
    bool DirtyTreeOverwriteProtection,
    bool AutomaticResetEnabled,
    bool CleanupRequiresConfirmation,
    string WorktreeRoot);

public sealed record CreateDevelopmentWorktreeRequest(
    string RepositoryPath,
    string Branch,
    string StartPoint,
    string WorktreeId,
    bool ConfirmCreateWorktree = false);

public sealed record RemoveDevelopmentWorktreeRequest(
    string RepositoryPath,
    string WorktreePath,
    bool ConfirmRemoveWorktree = false);

public sealed record DevelopmentWorktreeInfo(
    string RepositoryPath,
    string WorktreePath,
    string Branch,
    string Head,
    bool IsMainWorktree,
    bool IsDirty);

public sealed record DevelopmentWorktreeCreateResult(
    string RepositoryPath,
    string WorktreePath,
    string Branch,
    string StartPoint,
    bool Created,
    DateTimeOffset CreatedAt);

public sealed record DevelopmentWorktreeRemoveResult(
    string RepositoryPath,
    string WorktreePath,
    bool Removed,
    DateTimeOffset RemovedAt);

public sealed class DevelopmentWorktreeValidationException(string message)
    : Exception(message);
