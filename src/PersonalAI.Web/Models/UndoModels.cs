namespace PersonalAI.Web.Models;

public static class UndoStatuses
{
    public const string Pending = "pending";
    public const string Available = "available";
    public const string Undone = "undone";
    public const string Abandoned = "abandoned";
    public const string Expired = "expired";
}

public static class UndoOperations
{
    public const string DeleteCreatedFile = "delete-created-file";
    public const string RestoreFile = "restore-file";
    public const string RestoreDeletedFile = "restore-deleted-file";
    public const string DeleteCreatedDirectory = "delete-created-directory";
    public const string RestoreEmptyDirectory = "restore-empty-directory";
    public const string MoveFileBack = "move-file-back";
}

public sealed record UndoItem(
    Guid UndoId,
    Guid InvocationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string WorkspaceId,
    string ToolName,
    string Operation,
    string PrimaryPath,
    string? SecondaryPath,
    string Status,
    bool RequiresConfirmation = true);

public sealed record UndoListResponse(
    string Storage,
    int AvailabilityDays,
    int MaximumEntries,
    int MaximumSnapshotBytes,
    IReadOnlyList<UndoItem> Items);

public sealed record UndoAssessment(
    Guid UndoId,
    string Status,
    bool CanUndo,
    string Reason,
    string Operation,
    string PrimaryPath,
    string? SecondaryPath,
    DateTimeOffset ExpiresAt);

public sealed record ExecuteUndoRequest(bool Confirmed = false);

public sealed record UndoExecutionResponse(
    UndoItem Undo,
    string Status,
    string Message);

internal sealed record UndoPreparation(
    Guid UndoId,
    Guid InvocationId,
    string WorkspaceId,
    string ToolName,
    string Operation,
    string PrimaryPath,
    string? SecondaryPath,
    string? BeforeSha256,
    byte[]? SnapshotBytes);

internal sealed record StoredUndoItem(
    Guid UndoId,
    Guid InvocationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string WorkspaceId,
    string ToolName,
    string Operation,
    string PrimaryPath,
    string? SecondaryPath,
    string? BeforeSha256,
    string? PostSha256,
    byte[]? SnapshotBytes,
    string Status);

internal sealed record WorkspaceUndoCapture(
    string Operation,
    string PrimaryPath,
    string? SecondaryPath,
    string? BeforeSha256,
    byte[]? SnapshotBytes);

internal sealed record WorkspaceUndoCheck(
    bool CanUndo,
    string Reason);

internal sealed record WorkspaceUndoApplyResult(
    string Message);
