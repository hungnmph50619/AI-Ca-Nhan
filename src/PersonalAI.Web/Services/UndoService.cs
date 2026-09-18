using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IUndoService
{
    Task<UndoPreparation?> PrepareAsync(
        Guid invocationId,
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default);

    Guid? Complete(
        UndoPreparation? preparation,
        ToolExecutionResponse response);

    void Abandon(UndoPreparation? preparation);

    UndoListResponse GetRecent(int limit = 50);

    Task<UndoAssessment?> AssessAsync(
        Guid undoId,
        CancellationToken cancellationToken = default);

    Task<UndoExecutionResponse> ExecuteAsync(
        Guid undoId,
        bool confirmed,
        CancellationToken cancellationToken = default);
}

public sealed class UndoConflictException(string message) : Exception(message);

public sealed class UndoConfirmationRequiredException(string message) : Exception(message);

public sealed class UndoService(
    IUndoStore store,
    IWorkspaceFileService workspaceFiles,
    IWorkspaceContextAccessor workspaceContext,
    IAuditRecorder audit,
    ILogger<UndoService> logger) : IUndoService
{
    public async Task<UndoPreparation?> PrepareAsync(
        Guid invocationId,
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var capture = await workspaceFiles.CaptureUndoAsync(
            toolName,
            arguments,
            cancellationToken);
        if (capture is null)
        {
            return null;
        }

        var preparation = new UndoPreparation(
            Guid.NewGuid(),
            invocationId,
            workspaceContext.CurrentWorkspaceId,
            toolName,
            capture.Operation,
            capture.PrimaryPath,
            capture.SecondaryPath,
            capture.BeforeSha256,
            capture.SnapshotBytes);

        return store.CreatePending(preparation);
    }

    public Guid? Complete(
        UndoPreparation? preparation,
        ToolExecutionResponse response)
    {
        if (preparation is null)
        {
            return null;
        }

        if (!response.Success
            || response.Status != ToolExecutionStatuses.Succeeded
            || response.Output is null)
        {
            store.Abandon(preparation.UndoId);
            return null;
        }

        try
        {
            var output = response.Output.Value;
            string? postSha256 = null;
            var applicable = preparation.Operation switch
            {
                UndoOperations.DeleteCreatedFile =>
                    ReadBoolean(output, "created")
                    && TryReadString(output, "sha256", out postSha256),
                UndoOperations.RestoreFile =>
                    HashMatchesOutput(
                        output,
                        "previousSha256",
                        preparation.BeforeSha256,
                        out _)
                    && TryReadString(
                        output,
                        "sha256",
                        out postSha256),
                UndoOperations.DeleteCreatedDirectory =>
                    ReadBoolean(output, "created"),
                UndoOperations.RestoreDeletedFile =>
                    ReadBoolean(output, "deleted")
                    && ReadString(output, "type") == "file"
                    && HashMatchesOutput(
                        output,
                        "sha256",
                        preparation.BeforeSha256,
                        out _),
                UndoOperations.RestoreEmptyDirectory =>
                    ReadBoolean(output, "deleted")
                    && ReadString(output, "type") == "directory",
                UndoOperations.MoveFileBack =>
                    ReadString(output, "type") == "file"
                    && HashMatchesOutput(
                        output,
                        "sha256",
                        preparation.BeforeSha256,
                        out postSha256),
                _ => false
            };

            if (!applicable)
            {
                store.Abandon(preparation.UndoId);
                return null;
            }

            store.MarkAvailable(
                preparation.UndoId,
                postSha256);

            audit.Record(
                AuditAgents.System,
                "undo.available",
                $"undo:{preparation.UndoId:D}",
                "reversible-tool-action",
                AuditResults.Prepared,
                preparation.ToolName,
                preparation.WorkspaceId);

            return preparation.UndoId;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Không thể hoàn tất bản ghi undo {UndoId} cho invocation {InvocationId}.",
                preparation.UndoId,
                preparation.InvocationId);
            store.Abandon(preparation.UndoId);
            return null;
        }
    }

    public void Abandon(
        UndoPreparation? preparation)
    {
        if (preparation is null)
        {
            return;
        }

        try
        {
            store.Abandon(preparation.UndoId);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Không thể hủy bản ghi undo đang chuẩn bị {UndoId}.",
                preparation.UndoId);
        }
    }

    public UndoListResponse GetRecent(
        int limit = 50) =>
        store.GetRecent(
            workspaceContext.CurrentWorkspaceId,
            limit);

    public async Task<UndoAssessment?> AssessAsync(
        Guid undoId,
        CancellationToken cancellationToken = default)
    {
        var item = store.Get(
            undoId,
            workspaceContext.CurrentWorkspaceId);
        if (item is null)
        {
            return null;
        }

        if (item.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return ToAssessment(
                item,
                UndoStatuses.Expired,
                false,
                "Thời hạn hoàn tác đã hết.");
        }

        if (item.Status != UndoStatuses.Available)
        {
            return ToAssessment(
                item,
                item.Status,
                false,
                StatusReason(item.Status));
        }

        var check = await workspaceFiles.AssessUndoAsync(
            item,
            cancellationToken);
        return ToAssessment(
            item,
            item.Status,
            check.CanUndo,
            check.Reason);
    }

    public async Task<UndoExecutionResponse> ExecuteAsync(
        Guid undoId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            audit.Record(
                AuditAgents.User,
                "undo.execute",
                $"undo:{undoId:D}",
                "confirmation-required",
                AuditResults.Denied);
            throw new UndoConfirmationRequiredException(
                "Hoàn tác có thể thay đổi tệp. Hãy xác nhận rõ ràng trước khi thực hiện.");
        }

        var item = store.Get(
            undoId,
            workspaceContext.CurrentWorkspaceId)
            ?? throw new KeyNotFoundException(
                "Không tìm thấy bản ghi hoàn tác trong workspace hiện tại.");

        if (item.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            RecordDenied(item, "undo-expired");
            throw new UndoConflictException(
                "Thời hạn hoàn tác đã hết.");
        }

        if (item.Status != UndoStatuses.Available)
        {
            RecordDenied(item, "undo-not-available");
            throw new UndoConflictException(
                StatusReason(item.Status));
        }

        try
        {
            var result = await workspaceFiles.ApplyUndoAsync(
                item,
                cancellationToken);
            var publicItem = store.MarkUndone(
                item.UndoId,
                item.WorkspaceId);

            audit.Record(
                AuditAgents.User,
                "undo.execute",
                $"undo:{item.UndoId:D}",
                "user-confirmed-undo",
                AuditResults.Succeeded,
                item.ToolName,
                item.WorkspaceId);

            return new UndoExecutionResponse(
                publicItem,
                UndoStatuses.Undone,
                result.Message);
        }
        catch (UndoConflictException)
        {
            RecordDenied(item, "undo-precondition-failed");
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Không thể áp dụng undo {UndoId} do trạng thái filesystem.",
                item.UndoId);
            RecordDenied(item, "undo-filesystem-conflict");
            throw new UndoConflictException(
                "Không thể hoàn tác vì trạng thái hoặc quyền truy cập của tệp đã thay đổi.");
        }
    }

    private void RecordDenied(
        StoredUndoItem item,
        string reason)
    {
        audit.Record(
            AuditAgents.User,
            "undo.execute",
            $"undo:{item.UndoId:D}",
            reason,
            AuditResults.Denied,
            item.ToolName,
            item.WorkspaceId);
    }

    private static UndoAssessment ToAssessment(
        StoredUndoItem item,
        string status,
        bool canUndo,
        string reason) =>
        new(
            item.UndoId,
            status,
            canUndo,
            reason,
            item.Operation,
            item.PrimaryPath,
            item.SecondaryPath,
            item.ExpiresAt);

    private static string StatusReason(string status) =>
        status switch
        {
            UndoStatuses.Pending =>
                "Hành động gốc chưa hoàn tất việc chuẩn bị hoàn tác.",
            UndoStatuses.Undone =>
                "Hành động này đã được hoàn tác.",
            UndoStatuses.Abandoned =>
                "Bản ghi hoàn tác đã bị hủy vì hành động gốc không hoàn tất.",
            UndoStatuses.Expired =>
                "Thời hạn hoàn tác đã hết.",
            _ =>
                "Bản ghi không ở trạng thái có thể hoàn tác."
        };

    private static bool ReadBoolean(
        JsonElement element,
        string property) =>
        TryGetPropertyIgnoreCase(element, property, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static string ReadString(
        JsonElement element,
        string property) =>
        TryGetPropertyIgnoreCase(element, property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string property,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(property, out value))
            {
                return true;
            }

            foreach (var candidate in element.EnumerateObject())
            {
                if (candidate.Name.Equals(
                    property,
                    StringComparison.OrdinalIgnoreCase))
                {
                    value = candidate.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool HashMatchesOutput(
        JsonElement element,
        string property,
        string? expected,
        out string? actual)
    {
        actual = null;
        return !string.IsNullOrWhiteSpace(expected)
            && TryReadString(
                element,
                property,
                out actual)
            && string.Equals(
                actual,
                expected,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadString(
        JsonElement element,
        string property,
        out string? value)
    {
        value = ReadString(element, property);
        if (string.IsNullOrWhiteSpace(value))
        {
            value = null;
            return false;
        }

        return true;
    }
}
