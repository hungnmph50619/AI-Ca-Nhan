using Microsoft.Data.Sqlite;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IAuditStore
{
    void Record(AuditEvent auditEvent);

    AuditItem? GetById(
        string workspaceId,
        Guid auditId);

    AuditResponse GetRecent(
        string workspaceId,
        int limit = 50,
        string? agent = null,
        string? action = null,
        string? result = null,
        string? level = null,
        string? source = null,
        string? correlationId = null);

    AuditSummary GetSummary(string workspaceId);
}

public interface IAuditRecorder
{
    void Record(
        string agent,
        string action,
        string target,
        string reason,
        string result,
        string? tool = null,
        string? workspaceId = null,
        string? level = null,
        string? source = null,
        string? correlationId = null);
}

public sealed class AuditRecorder(
    IAuditStore auditStore,
    IWorkspaceContextAccessor workspaceContext,
    ILogger<AuditRecorder> logger) : IAuditRecorder
{
    public void Record(
        string agent,
        string action,
        string target,
        string reason,
        string result,
        string? tool = null,
        string? workspaceId = null,
        string? level = null,
        string? source = null,
        string? correlationId = null)
    {
        try
        {
            auditStore.Record(new AuditEvent(
                string.IsNullOrWhiteSpace(workspaceId)
                    ? workspaceContext.CurrentWorkspaceId
                    : workspaceId.Trim().ToLowerInvariant(),
                agent,
                action,
                target,
                reason,
                result,
                tool,
                level,
                source,
                correlationId));
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Không thể ghi audit event {AuditAction} cho {AuditTarget}.",
                action,
                target);
        }
    }
}

public sealed class SqliteAuditStore : IAuditStore
{
    public const int RetentionDays = 90;
    public const int MaximumEntries = 10_000;
    public const int MaximumQueryLimit = 200;

    private readonly object _gate = new();
    private readonly string _databasePath;
    private bool _initialized;

    public SqliteAuditStore(IConfiguration configuration)
    {
        var configuredRoot = configuration["Audit:Root"]?.Trim();
        string directory;

        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            directory = Path.GetFullPath(configuredRoot);
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
                "Audit");
        }

        Directory.CreateDirectory(directory);
        _databasePath = Path.Combine(directory, "audit.db");
    }

    public void Record(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var workspaceId = NormalizeRequired(
            auditEvent.WorkspaceId, 80, "workspace");
        var agent = NormalizeRequired(
            auditEvent.Agent, 80, "agent");
        var action = NormalizeRequired(
            auditEvent.Action, 120, "action");
        var target = NormalizeRequired(
            auditEvent.Target, 240, "target");
        var reason = NormalizeRequired(
            auditEvent.Reason, 240, "reason");
        var result = NormalizeRequired(
            auditEvent.Result, 80, "result");
        var tool = NormalizeNullable(auditEvent.Tool, 160);
        var level = NormalizeLevel(auditEvent.Level, result);
        var source = NormalizeNullable(auditEvent.Source, 120) ?? agent;
        var correlationId = NormalizeNullable(
            auditEvent.CorrelationId, 160);

        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    INSERT INTO audit_events (
                        audit_id,
                        occurred_at,
                        workspace_id,
                        agent,
                        action,
                        tool,
                        target,
                        reason,
                        result,
                        level,
                        source,
                        correlation_id
                    )
                    VALUES (
                        $auditId,
                        $occurredAt,
                        $workspaceId,
                        $agent,
                        $action,
                        $tool,
                        $target,
                        $reason,
                        $result,
                        $level,
                        $source,
                        $correlationId
                    );
                    """;

                command.Parameters.AddWithValue(
                    "$auditId",
                    Guid.NewGuid().ToString("D"));
                command.Parameters.AddWithValue(
                    "$occurredAt",
                    DateTimeOffset.UtcNow.ToString("O"));
                command.Parameters.AddWithValue(
                    "$workspaceId",
                    workspaceId);
                command.Parameters.AddWithValue("$agent", agent);
                command.Parameters.AddWithValue("$action", action);
                command.Parameters.AddWithValue(
                    "$tool",
                    DbNullable(tool));
                command.Parameters.AddWithValue("$target", target);
                command.Parameters.AddWithValue("$reason", reason);
                command.Parameters.AddWithValue("$result", result);
                command.Parameters.AddWithValue("$level", level);
                command.Parameters.AddWithValue("$source", source);
                command.Parameters.AddWithValue(
                    "$correlationId",
                    DbNullable(correlationId));
                command.ExecuteNonQuery();
            }

            Cleanup(connection);
        }
    }

    public AuditItem? GetById(
        string workspaceId,
        Guid auditId)
    {
        var safeWorkspace = NormalizeRequired(
            workspaceId, 80, "workspace");

        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            Cleanup(connection);

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    audit_id,
                    occurred_at,
                    workspace_id,
                    agent,
                    action,
                    tool,
                    target,
                    reason,
                    result,
                    level,
                    source,
                    correlation_id
                FROM audit_events
                WHERE workspace_id = $workspaceId
                  AND audit_id = $auditId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue(
                "$workspaceId", safeWorkspace);
            command.Parameters.AddWithValue(
                "$auditId", auditId.ToString("D"));

            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return null;

            return ReadItem(reader);
        }
    }

    public AuditResponse GetRecent(
        string workspaceId,
        int limit = 50,
        string? agent = null,
        string? action = null,
        string? result = null,
        string? level = null,
        string? source = null,
        string? correlationId = null)
    {
        var safeWorkspace = NormalizeRequired(
            workspaceId, 80, "workspace");
        var safeLimit = Math.Clamp(limit, 1, MaximumQueryLimit);
        var safeAgent = NormalizeNullable(agent, 80);
        var safeAction = NormalizeNullable(action, 120);
        var safeResult = NormalizeNullable(result, 80);
        var safeLevel = NormalizeNullable(level, 20)?.ToLowerInvariant();
        if (safeLevel is not null && !SystemLogLevels.All.Contains(safeLevel))
            throw new ArgumentException("System log level không hợp lệ.");
        var safeSource = NormalizeNullable(source, 120);
        var safeCorrelation = NormalizeNullable(correlationId, 160);

        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            Cleanup(connection);

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    audit_id,
                    occurred_at,
                    workspace_id,
                    agent,
                    action,
                    tool,
                    target,
                    reason,
                    result,
                    level,
                    source,
                    correlation_id
                FROM audit_events
                WHERE workspace_id = $workspaceId
                  AND ($agent IS NULL OR agent = $agent)
                  AND ($action IS NULL OR action = $action)
                  AND ($result IS NULL OR result = $result)
                  AND ($level IS NULL OR level = $level)
                  AND ($source IS NULL OR source = $source)
                  AND ($correlationId IS NULL OR correlation_id = $correlationId)
                ORDER BY occurred_at DESC, rowid DESC
                LIMIT $limit;
                """;

            command.Parameters.AddWithValue(
                "$workspaceId", safeWorkspace);
            command.Parameters.AddWithValue(
                "$agent", DbNullable(safeAgent));
            command.Parameters.AddWithValue(
                "$action", DbNullable(safeAction));
            command.Parameters.AddWithValue(
                "$result", DbNullable(safeResult));
            command.Parameters.AddWithValue(
                "$level", DbNullable(safeLevel));
            command.Parameters.AddWithValue(
                "$source", DbNullable(safeSource));
            command.Parameters.AddWithValue(
                "$correlationId", DbNullable(safeCorrelation));
            command.Parameters.AddWithValue(
                "$limit", safeLimit);

            using var reader = command.ExecuteReader();
            var items = new List<AuditItem>();

            while (reader.Read())
                items.Add(ReadItem(reader));

            return new AuditResponse(
                "local-sqlite",
                RetentionDays,
                MaximumEntries,
                items);
        }
    }

    public AuditSummary GetSummary(string workspaceId)
    {
        var safeWorkspace = NormalizeRequired(
            workspaceId, 80, "workspace");

        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            Cleanup(connection);

            return new AuditSummary(
                safeWorkspace,
                CountAll(connection, safeWorkspace),
                CountBy(connection, safeWorkspace, "agent"),
                CountBy(connection, safeWorkspace, "result"),
                CountBy(connection, safeWorkspace, "action"),
                CountBy(connection, safeWorkspace, "level"),
                CountBy(connection, safeWorkspace, "source"));
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        using var connection = OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS audit_events (
                    audit_id TEXT PRIMARY KEY,
                    occurred_at TEXT NOT NULL,
                    workspace_id TEXT NOT NULL,
                    agent TEXT NOT NULL,
                    action TEXT NOT NULL,
                    tool TEXT NULL,
                    target TEXT NOT NULL,
                    reason TEXT NOT NULL,
                    result TEXT NOT NULL,
                    level TEXT NULL,
                    source TEXT NULL,
                    correlation_id TEXT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        EnsureColumn(connection, "level", "TEXT NULL");
        EnsureColumn(connection, "source", "TEXT NULL");
        EnsureColumn(connection, "correlation_id", "TEXT NULL");

        using (var migrate = connection.CreateCommand())
        {
            migrate.CommandText =
                """
                UPDATE audit_events
                SET level = CASE
                    WHEN result = 'failed' THEN 'error'
                    WHEN result IN ('denied', 'blocked') THEN 'security'
                    ELSE 'info'
                END
                WHERE level IS NULL;

                UPDATE audit_events
                SET source = agent
                WHERE source IS NULL;
                """;
            migrate.ExecuteNonQuery();
        }

        using (var indexes = connection.CreateCommand())
        {
            indexes.CommandText =
                """
                CREATE INDEX IF NOT EXISTS ix_audit_events_workspace_time
                    ON audit_events(workspace_id, occurred_at DESC);

                CREATE INDEX IF NOT EXISTS ix_audit_events_action
                    ON audit_events(action);

                CREATE INDEX IF NOT EXISTS ix_audit_events_agent
                    ON audit_events(agent);

                CREATE INDEX IF NOT EXISTS ix_audit_events_level
                    ON audit_events(level);

                CREATE INDEX IF NOT EXISTS ix_audit_events_source
                    ON audit_events(source);

                CREATE INDEX IF NOT EXISTS ix_audit_events_correlation
                    ON audit_events(correlation_id);
                """;
            indexes.ExecuteNonQuery();
        }

        _initialized = true;
    }

    private static void EnsureColumn(
        SqliteConnection connection,
        string column,
        string definition)
    {
        using var inspect = connection.CreateCommand();
        inspect.CommandText = "PRAGMA table_info(audit_events);";
        using var reader = inspect.ExecuteReader();
        var exists = false;
        while (reader.Read())
        {
            if (reader.GetString(1).Equals(
                column,
                StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        reader.Close();
        if (exists)
            return;

        using var alter = connection.CreateCommand();
        alter.CommandText =
            $"ALTER TABLE audit_events ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
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
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                DELETE FROM audit_events
                WHERE occurred_at < $cutoff;
                """;
            command.Parameters.AddWithValue(
                "$cutoff",
                DateTimeOffset.UtcNow
                    .AddDays(-RetentionDays)
                    .ToString("O"));
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                DELETE FROM audit_events
                WHERE rowid IN (
                    SELECT rowid
                    FROM audit_events
                    ORDER BY occurred_at DESC, rowid DESC
                    LIMIT -1 OFFSET $maximumEntries
                );
                """;
            command.Parameters.AddWithValue(
                "$maximumEntries",
                MaximumEntries);
            command.ExecuteNonQuery();
        }
    }

    private static int CountAll(
        SqliteConnection connection,
        string workspaceId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM audit_events
            WHERE workspace_id = $workspaceId;
            """;
        command.Parameters.AddWithValue(
            "$workspaceId", workspaceId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static IReadOnlyDictionary<string, int> CountBy(
        SqliteConnection connection,
        string workspaceId,
        string column)
    {
        if (column is not (
            "agent" or "result" or "action" or "level" or "source"))
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {column}, COUNT(*)
            FROM audit_events
            WHERE workspace_id = $workspaceId
              AND {column} IS NOT NULL
            GROUP BY {column}
            ORDER BY COUNT(*) DESC, {column} ASC;
            """;
        command.Parameters.AddWithValue(
            "$workspaceId", workspaceId);

        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
            result[reader.GetString(0)] = reader.GetInt32(1);

        return result;
    }

    private static AuditItem ReadItem(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            DateTimeOffset.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11));

    private static string NormalizeLevel(
        string? value,
        string result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return result.ToLowerInvariant() switch
            {
                AuditResults.Failed => SystemLogLevels.Error,
                AuditResults.Denied or AuditResults.Blocked =>
                    SystemLogLevels.Security,
                _ => SystemLogLevels.Info
            };
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!SystemLogLevels.All.Contains(normalized))
            throw new ArgumentException("Audit level không hợp lệ.");
        return normalized;
    }

    private static string NormalizeRequired(
        string? value,
        int maximumLength,
        string field)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                $"Audit {field} không được để trống.");
        }

        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private static string? NormalizeNullable(
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private static object DbNullable(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value;
}
