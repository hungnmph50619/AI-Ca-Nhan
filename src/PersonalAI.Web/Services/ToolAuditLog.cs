using Microsoft.Data.Sqlite;
using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IToolAuditLog
{
    Task UpsertAsync(
        ToolAuditEntry entry,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ToolAuditEntry>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed class SqliteToolAuditLog : IToolAuditLog
{
    public const int MaximumQueryLimit = 100;

    private readonly string _databasePath;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public SqliteToolAuditLog()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalAI",
            "Tools");
        _databasePath = Path.Combine(root, "tool-audit.db");
    }

    public async Task UpsertAsync(
        ToolAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new SqliteConnection(
            $"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Shared");
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO tool_audit (
                invocation_id,
                tool_name,
                tool_version,
                status,
                success,
                started_at,
                completed_at,
                duration_ms,
                required_permissions,
                approved_permissions,
                confirmed,
                actor,
                input_sha256,
                input_bytes,
                output_sha256,
                output_bytes,
                reversibility,
                local_only,
                error)
            VALUES (
                $invocationId,
                $toolName,
                $toolVersion,
                $status,
                $success,
                $startedAt,
                $completedAt,
                $durationMs,
                $requiredPermissions,
                $approvedPermissions,
                $confirmed,
                $actor,
                $inputSha256,
                $inputBytes,
                $outputSha256,
                $outputBytes,
                $reversibility,
                $localOnly,
                $error)
            ON CONFLICT(invocation_id) DO UPDATE SET
                tool_name = excluded.tool_name,
                tool_version = excluded.tool_version,
                status = excluded.status,
                success = excluded.success,
                started_at = excluded.started_at,
                completed_at = excluded.completed_at,
                duration_ms = excluded.duration_ms,
                required_permissions = excluded.required_permissions,
                approved_permissions = excluded.approved_permissions,
                confirmed = excluded.confirmed,
                actor = excluded.actor,
                input_sha256 = excluded.input_sha256,
                input_bytes = excluded.input_bytes,
                output_sha256 = excluded.output_sha256,
                output_bytes = excluded.output_bytes,
                reversibility = excluded.reversibility,
                local_only = excluded.local_only,
                error = excluded.error;
            """;

        command.Parameters.AddWithValue("$invocationId", entry.InvocationId.ToString("D"));
        command.Parameters.AddWithValue("$toolName", entry.ToolName);
        command.Parameters.AddWithValue("$toolVersion", entry.ToolVersion);
        command.Parameters.AddWithValue("$status", entry.Status);
        command.Parameters.AddWithValue(
            "$success",
            entry.Success is null ? DBNull.Value : entry.Success.Value ? 1 : 0);
        command.Parameters.AddWithValue("$startedAt", entry.StartedAt.ToString("O"));
        command.Parameters.AddWithValue(
            "$completedAt",
            entry.CompletedAt is null ? DBNull.Value : entry.CompletedAt.Value.ToString("O"));
        command.Parameters.AddWithValue(
            "$durationMs",
            entry.DurationMs is null ? DBNull.Value : entry.DurationMs.Value);
        command.Parameters.AddWithValue(
            "$requiredPermissions",
            JsonSerializer.Serialize(entry.RequiredPermissions));
        command.Parameters.AddWithValue(
            "$approvedPermissions",
            JsonSerializer.Serialize(entry.ApprovedPermissions));
        command.Parameters.AddWithValue("$confirmed", entry.Confirmed ? 1 : 0);
        command.Parameters.AddWithValue("$actor", entry.Actor);
        command.Parameters.AddWithValue("$inputSha256", entry.InputSha256);
        command.Parameters.AddWithValue("$inputBytes", entry.InputBytes);
        command.Parameters.AddWithValue(
            "$outputSha256",
            entry.OutputSha256 is null ? DBNull.Value : entry.OutputSha256);
        command.Parameters.AddWithValue(
            "$outputBytes",
            entry.OutputBytes is null ? DBNull.Value : entry.OutputBytes.Value);
        command.Parameters.AddWithValue("$reversibility", entry.Reversibility);
        command.Parameters.AddWithValue("$localOnly", entry.LocalOnly ? 1 : 0);
        command.Parameters.AddWithValue(
            "$error",
            string.IsNullOrWhiteSpace(entry.Error) ? DBNull.Value : entry.Error);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ToolAuditEntry>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        limit = Math.Clamp(limit, 1, MaximumQueryLimit);

        await using var connection = new SqliteConnection(
            $"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Shared");
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                invocation_id,
                tool_name,
                tool_version,
                status,
                success,
                started_at,
                completed_at,
                duration_ms,
                required_permissions,
                approved_permissions,
                confirmed,
                actor,
                input_sha256,
                input_bytes,
                output_sha256,
                output_bytes,
                reversibility,
                local_only,
                error
            FROM tool_audit
            ORDER BY started_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<ToolAuditEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ToolAuditEntry(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4) == 1,
                DateTimeOffset.Parse(reader.GetString(5)),
                reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : checked((int)reader.GetInt64(7)),
                DeserializePermissions(reader.GetString(8)),
                DeserializePermissions(reader.GetString(9)),
                reader.GetInt64(10) == 1,
                reader.GetString(11),
                reader.GetString(12),
                checked((int)reader.GetInt64(13)),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : checked((int)reader.GetInt64(15)),
                reader.GetString(16),
                reader.GetInt64(17) == 1,
                reader.IsDBNull(18) ? null : reader.GetString(18)));
        }

        return results;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

            await using var connection = new SqliteConnection(
                $"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Shared");
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA journal_mode = WAL;
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS tool_audit (
                    invocation_id TEXT PRIMARY KEY,
                    tool_name TEXT NOT NULL,
                    tool_version TEXT NOT NULL,
                    status TEXT NOT NULL,
                    success INTEGER NULL,
                    started_at TEXT NOT NULL,
                    completed_at TEXT NULL,
                    duration_ms INTEGER NULL,
                    required_permissions TEXT NOT NULL,
                    approved_permissions TEXT NOT NULL,
                    confirmed INTEGER NOT NULL,
                    actor TEXT NOT NULL,
                    input_sha256 TEXT NOT NULL,
                    input_bytes INTEGER NOT NULL,
                    output_sha256 TEXT NULL,
                    output_bytes INTEGER NULL,
                    reversibility TEXT NOT NULL,
                    local_only INTEGER NOT NULL,
                    error TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_tool_audit_started_at
                ON tool_audit(started_at DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static IReadOnlyList<string> DeserializePermissions(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(value) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
