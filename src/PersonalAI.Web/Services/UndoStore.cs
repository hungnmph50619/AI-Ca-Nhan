using Microsoft.Data.Sqlite;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IUndoStore
{
    UndoPreparation CreatePending(UndoPreparation preparation);
    void MarkAvailable(Guid undoId, string? postSha256);
    void Abandon(Guid undoId);
    StoredUndoItem? Get(Guid undoId, string workspaceId);
    UndoListResponse GetRecent(string workspaceId, int limit = 50);
    UndoItem MarkUndone(Guid undoId, string workspaceId);
}

public sealed class SqliteUndoStore : IUndoStore
{
    public const int AvailabilityDays = 7;
    public const int MaximumEntries = 500;
    public const int MaximumQueryLimit = 100;
    public const int MaximumSnapshotBytes = 512 * 1024;

    private readonly object _gate = new();
    private readonly string _databasePath;
    private bool _initialized;

    public SqliteUndoStore(IConfiguration configuration)
    {
        var configuredRoot = configuration["Undo:Root"]?.Trim();
        string directory;

        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            directory = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(configuredRoot));
        }
        else
        {
            var localData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localData))
            {
                localData = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile),
                    ".personalai");
            }

            directory = Path.Combine(
                localData,
                "PersonalAI",
                "Undo");
        }

        Directory.CreateDirectory(directory);
        _databasePath = Path.Combine(directory, "undo.db");
    }

    public UndoPreparation CreatePending(UndoPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (preparation.SnapshotBytes is { Length: > MaximumSnapshotBytes })
        {
            throw new InvalidOperationException(
                $"Ảnh chụp hoàn tác vượt giới hạn {MaximumSnapshotBytes / 1024} KB.");
        }

        var createdAt = DateTimeOffset.UtcNow;
        var expiresAt = createdAt.AddDays(AvailabilityDays);

        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            Cleanup(connection);

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO undo_entries (
                    undo_id,
                    invocation_id,
                    created_at,
                    expires_at,
                    workspace_id,
                    tool_name,
                    operation,
                    primary_path,
                    secondary_path,
                    before_sha256,
                    post_sha256,
                    snapshot,
                    status
                )
                VALUES (
                    $undoId,
                    $invocationId,
                    $createdAt,
                    $expiresAt,
                    $workspaceId,
                    $toolName,
                    $operation,
                    $primaryPath,
                    $secondaryPath,
                    $beforeSha256,
                    NULL,
                    $snapshot,
                    $status
                );
                """;
            command.Parameters.AddWithValue(
                "$undoId",
                preparation.UndoId.ToString("D"));
            command.Parameters.AddWithValue(
                "$invocationId",
                preparation.InvocationId.ToString("D"));
            command.Parameters.AddWithValue(
                "$createdAt",
                createdAt.ToString("O"));
            command.Parameters.AddWithValue(
                "$expiresAt",
                expiresAt.ToString("O"));
            command.Parameters.AddWithValue(
                "$workspaceId",
                NormalizeWorkspace(preparation.WorkspaceId));
            command.Parameters.AddWithValue(
                "$toolName",
                Limit(preparation.ToolName, 160));
            command.Parameters.AddWithValue(
                "$operation",
                Limit(preparation.Operation, 120));
            command.Parameters.AddWithValue(
                "$primaryPath",
                Limit(preparation.PrimaryPath, 500));
            command.Parameters.AddWithValue(
                "$secondaryPath",
                DbNullable(LimitNullable(
                    preparation.SecondaryPath,
                    500)));
            command.Parameters.AddWithValue(
                "$beforeSha256",
                DbNullable(LimitNullable(
                    preparation.BeforeSha256,
                    64)));
            command.Parameters.Add(
                "$snapshot",
                SqliteType.Blob).Value =
                preparation.SnapshotBytes is null
                    ? DBNull.Value
                    : preparation.SnapshotBytes;
            command.Parameters.AddWithValue(
                "$status",
                UndoStatuses.Pending);
            command.ExecuteNonQuery();

            return preparation;
        }
    }

    public void MarkAvailable(Guid undoId, string? postSha256)
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE undo_entries
                SET status = $status,
                    post_sha256 = $postSha256
                WHERE undo_id = $undoId
                  AND status = $pending;
                """;
            command.Parameters.AddWithValue(
                "$status",
                UndoStatuses.Available);
            command.Parameters.AddWithValue(
                "$postSha256",
                DbNullable(LimitNullable(postSha256, 64)));
            command.Parameters.AddWithValue(
                "$undoId",
                undoId.ToString("D"));
            command.Parameters.AddWithValue(
                "$pending",
                UndoStatuses.Pending);
            command.ExecuteNonQuery();
        }
    }

    public void Abandon(Guid undoId)
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE undo_entries
                SET status = $status,
                    snapshot = NULL
                WHERE undo_id = $undoId
                  AND status = $pending;
                """;
            command.Parameters.AddWithValue(
                "$status",
                UndoStatuses.Abandoned);
            command.Parameters.AddWithValue(
                "$undoId",
                undoId.ToString("D"));
            command.Parameters.AddWithValue(
                "$pending",
                UndoStatuses.Pending);
            command.ExecuteNonQuery();
        }
    }

    public StoredUndoItem? Get(
        Guid undoId,
        string workspaceId)
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            Cleanup(connection);

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    undo_id,
                    invocation_id,
                    created_at,
                    expires_at,
                    workspace_id,
                    tool_name,
                    operation,
                    primary_path,
                    secondary_path,
                    before_sha256,
                    post_sha256,
                    snapshot,
                    status
                FROM undo_entries
                WHERE undo_id = $undoId
                  AND workspace_id = $workspaceId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue(
                "$undoId",
                undoId.ToString("D"));
            command.Parameters.AddWithValue(
                "$workspaceId",
                NormalizeWorkspace(workspaceId));

            using var reader = command.ExecuteReader();
            return reader.Read()
                ? ReadStored(reader)
                : null;
        }
    }

    public UndoListResponse GetRecent(
        string workspaceId,
        int limit = 50)
    {
        var safeLimit = Math.Clamp(
            limit,
            1,
            MaximumQueryLimit);

        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            Cleanup(connection);

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    undo_id,
                    invocation_id,
                    created_at,
                    expires_at,
                    workspace_id,
                    tool_name,
                    operation,
                    primary_path,
                    secondary_path,
                    before_sha256,
                    post_sha256,
                    snapshot,
                    status
                FROM undo_entries
                WHERE workspace_id = $workspaceId
                  AND status != $abandoned
                ORDER BY created_at DESC, rowid DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue(
                "$workspaceId",
                NormalizeWorkspace(workspaceId));
            command.Parameters.AddWithValue(
                "$abandoned",
                UndoStatuses.Abandoned);
            command.Parameters.AddWithValue(
                "$limit",
                safeLimit);

            using var reader = command.ExecuteReader();
            var items = new List<UndoItem>();
            while (reader.Read())
            {
                items.Add(ToPublic(ReadStored(reader)));
            }

            return new UndoListResponse(
                "local-sqlite",
                AvailabilityDays,
                MaximumEntries,
                MaximumSnapshotBytes,
                items);
        }
    }

    public UndoItem MarkUndone(
        Guid undoId,
        string workspaceId)
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    UPDATE undo_entries
                    SET status = $status,
                        snapshot = NULL
                    WHERE undo_id = $undoId
                      AND workspace_id = $workspaceId
                      AND status = $available;
                    """;
                command.Parameters.AddWithValue(
                    "$status",
                    UndoStatuses.Undone);
                command.Parameters.AddWithValue(
                    "$undoId",
                    undoId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$workspaceId",
                    NormalizeWorkspace(workspaceId));
                command.Parameters.AddWithValue(
                    "$available",
                    UndoStatuses.Available);

                if (command.ExecuteNonQuery() != 1)
                {
                    throw new InvalidOperationException(
                        "Bản ghi hoàn tác không còn ở trạng thái có thể hoàn tác.");
                }
            }

            StoredUndoItem stored;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    SELECT
                        undo_id,
                        invocation_id,
                        created_at,
                        expires_at,
                        workspace_id,
                        tool_name,
                        operation,
                        primary_path,
                        secondary_path,
                        before_sha256,
                        post_sha256,
                        snapshot,
                        status
                    FROM undo_entries
                    WHERE undo_id = $undoId
                      AND workspace_id = $workspaceId
                    LIMIT 1;
                    """;
                command.Parameters.AddWithValue(
                    "$undoId",
                    undoId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$workspaceId",
                    NormalizeWorkspace(workspaceId));

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    throw new InvalidOperationException(
                        "Không tìm thấy bản ghi hoàn tác sau khi cập nhật.");
                }

                stored = ReadStored(reader);
            }

            transaction.Commit();
            return ToPublic(stored);
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS undo_entries (
                undo_id TEXT PRIMARY KEY,
                invocation_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                workspace_id TEXT NOT NULL,
                tool_name TEXT NOT NULL,
                operation TEXT NOT NULL,
                primary_path TEXT NOT NULL,
                secondary_path TEXT NULL,
                before_sha256 TEXT NULL,
                post_sha256 TEXT NULL,
                snapshot BLOB NULL,
                status TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_undo_workspace_created
                ON undo_entries(workspace_id, created_at DESC);

            CREATE INDEX IF NOT EXISTS ix_undo_invocation
                ON undo_entries(invocation_id);
            """;
        command.ExecuteNonQuery();
        _initialized = true;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(
            $"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Shared");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void Cleanup(SqliteConnection connection)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                UPDATE undo_entries
                SET status = $expired,
                    snapshot = NULL
                WHERE status = $available
                  AND expires_at <= $now;
                """;
            command.Parameters.AddWithValue(
                "$expired",
                UndoStatuses.Expired);
            command.Parameters.AddWithValue(
                "$available",
                UndoStatuses.Available);
            command.Parameters.AddWithValue(
                "$now",
                now);
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                UPDATE undo_entries
                SET status = $abandoned,
                    snapshot = NULL
                WHERE status = $pending
                  AND created_at <= $cutoff;
                """;
            command.Parameters.AddWithValue(
                "$abandoned",
                UndoStatuses.Abandoned);
            command.Parameters.AddWithValue(
                "$pending",
                UndoStatuses.Pending);
            command.Parameters.AddWithValue(
                "$cutoff",
                DateTimeOffset.UtcNow
                    .AddMinutes(-10)
                    .ToString("O"));
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                DELETE FROM undo_entries
                WHERE rowid IN (
                    SELECT rowid
                    FROM undo_entries
                    ORDER BY created_at DESC, rowid DESC
                    LIMIT -1 OFFSET $maximumEntries
                );
                """;
            command.Parameters.AddWithValue(
                "$maximumEntries",
                MaximumEntries);
            command.ExecuteNonQuery();
        }
    }

    private static StoredUndoItem ReadStored(
        SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            DateTimeOffset.Parse(reader.GetString(2)),
            DateTimeOffset.Parse(reader.GetString(3)),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : (byte[])reader[11],
            reader.GetString(12));

    internal static UndoItem ToPublic(
        StoredUndoItem item)
    {
        var status = item.Status == UndoStatuses.Available
            && item.ExpiresAt <= DateTimeOffset.UtcNow
                ? UndoStatuses.Expired
                : item.Status;

        return new UndoItem(
            item.UndoId,
            item.InvocationId,
            item.CreatedAt,
            item.ExpiresAt,
            item.WorkspaceId,
            item.ToolName,
            item.Operation,
            item.PrimaryPath,
            item.SecondaryPath,
            status);
    }

    private static string NormalizeWorkspace(string value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (normalized.Length is < 1 or > 80)
        {
            throw new ArgumentException(
                "Workspace của bản ghi hoàn tác không hợp lệ.");
        }

        return normalized;
    }

    private static string Limit(
        string value,
        int maximum)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                "Metadata hoàn tác không được để trống.");
        }

        return normalized.Length <= maximum
            ? normalized
            : normalized[..maximum];
    }

    private static string? LimitNullable(
        string? value,
        int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximum
            ? normalized
            : normalized[..maximum];
    }

    private static object DbNullable(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value;
}
