namespace PersonalAI.Web.Models;

public static class DevelopmentRunWorktreeStates
{
    public const string Active = "active";
    public const string CleanupDeferredDirty = "cleanup-deferred-dirty";
    public const string Removed = "removed";
    public const string Missing = "missing";
}

public sealed record EnsureDevelopmentRunWorktreeRequest(
    Guid DevelopmentRunId,
    string StartPoint,
    bool ConfirmCreateWorktree = false);

public sealed record CleanupDevelopmentRunWorktreeRequest(
    Guid DevelopmentRunId,
    bool ConfirmCleanup = false);

public sealed record DevelopmentRunWorktreeBinding(
    Guid Id,
    string WorkspaceId,
    Guid DevelopmentRunId,
    string RepositoryPath,
    string Branch,
    string WorktreePath,
    string StartPoint,
    string State,
    bool Dirty,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? RemovedAt);

public sealed record DevelopmentRunWorktreeStatus(
    string Version,
    string WorkspaceId,
    int Bindings,
    int ActiveBindings,
    int DeferredDirtyBindings,
    int RemovedBindings,
    bool OneWorktreePerRun,
    bool CleanupRequiresTerminalRun,
    bool DirtyCleanupBlocked,
    bool ForceRemoveEnabled);

public sealed class DevelopmentRunWorktreeValidationException(string message)
    : Exception(message);
