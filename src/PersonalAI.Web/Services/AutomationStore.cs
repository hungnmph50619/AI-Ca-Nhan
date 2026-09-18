using System.Text.Json;
using Microsoft.Data.Sqlite;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IAutomationStore
{
    PersonalAutomation Save(PersonalAutomation automation);

    PersonalAutomation? Get(
        Guid automationId,
        string workspaceId);

    IReadOnlyList<PersonalAutomation> GetAll(
        string workspaceId);

    IReadOnlyList<PersonalAutomation> GetDue(
        DateTimeOffset now,
        int limit);

    bool Delete(
        Guid automationId,
        string workspaceId);

    int Count(
        string workspaceId);
}

public sealed class SqliteAutomationStore : IAutomationStore
{
    public const int MaximumAutomationsPerWorkspace = 50;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private readonly object _gate = new();
    private readonly string _databasePath;
    private readonly ILogger<SqliteAutomationStore> _logger;
    private bool _initialized;

    public SqliteAutomationStore(
        IConfiguration configuration,
        ILogger<SqliteAutomationStore> logger)
    {
        _logger = logger;

        var configuredRoot =
            configuration["Automation:Root"]?.Trim();
        string directory;

        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            directory = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(
                    configuredRoot));
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
                "Automation");
        }

        Directory.CreateDirectory(directory);
        _databasePath = Path.Combine(
            directory,
            "automations.db");
    }

    public PersonalAutomation Save(
        PersonalAutomation automation)
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            Upsert(connection, automation);
            return automation;
        }
    }

    public PersonalAutomation? Get(
        Guid automationId,
        string workspaceId)
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT payload_json
                FROM automations
                WHERE automation_id = $id
                  AND workspace_id = $workspace
                LIMIT 1;
                """;
            command.Parameters.AddWithValue(
                "$id",
                automationId.ToString("D"));
            command.Parameters.AddWithValue(
                "$workspace",
                NormalizeWorkspace(workspaceId));

            var payload =
                command.ExecuteScalar() as string;
            return payload is null
                ? null
                : Deserialize(payload, automationId);
        }
    }

    public IReadOnlyList<PersonalAutomation> GetAll(
        string workspaceId)
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT automation_id, payload_json
                FROM automations
                WHERE workspace_id = $workspace
                ORDER BY created_at DESC, rowid DESC;
                """;
            command.Parameters.AddWithValue(
                "$workspace",
                NormalizeWorkspace(workspaceId));

            using var reader = command.ExecuteReader();
            var result = new List<PersonalAutomation>();
            while (reader.Read())
            {
                var id = Guid.TryParse(
                    reader.GetString(0),
                    out var parsed)
                    ? parsed
                    : Guid.Empty;
                var item = Deserialize(
                    reader.GetString(1),
                    id);
                if (item is not null)
                {
                    result.Add(item);
                }
            }

            return result;
        }
    }

    public IReadOnlyList<PersonalAutomation> GetDue(
        DateTimeOffset now,
        int limit)
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT automation_id, payload_json
                FROM automations
                WHERE enabled = 1
                  AND state = $scheduled
                  AND next_run_at IS NOT NULL
                  AND next_run_at <= $now
                ORDER BY next_run_at ASC, created_at ASC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue(
                "$scheduled",
                AutomationStates.Scheduled);
            command.Parameters.AddWithValue(
                "$now",
                now.ToString("O"));
            command.Parameters.AddWithValue(
                "$limit",
                Math.Clamp(limit, 1, 100));

            using var reader = command.ExecuteReader();
            var result = new List<PersonalAutomation>();
            while (reader.Read())
            {
                var id = Guid.TryParse(
                    reader.GetString(0),
                    out var parsed)
                    ? parsed
                    : Guid.Empty;
                var item = Deserialize(
                    reader.GetString(1),
                    id);
                if (item is not null)
                {
                    result.Add(item);
                }
            }

            return result;
        }
    }

    public bool Delete(
        Guid automationId,
        string workspaceId)
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                DELETE FROM automations
                WHERE automation_id = $id
                  AND workspace_id = $workspace;
                """;
            command.Parameters.AddWithValue(
                "$id",
                automationId.ToString("D"));
            command.Parameters.AddWithValue(
                "$workspace",
                NormalizeWorkspace(workspaceId));
            return command.ExecuteNonQuery() > 0;
        }
    }

    public int Count(
        string workspaceId)
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT COUNT(*)
                FROM automations
                WHERE workspace_id = $workspace;
                """;
            command.Parameters.AddWithValue(
                "$workspace",
                NormalizeWorkspace(workspaceId));
            return Convert.ToInt32(
                command.ExecuteScalar());
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        using var connection = OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS automations (
                    automation_id TEXT PRIMARY KEY,
                    workspace_id TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    enabled INTEGER NOT NULL,
                    state TEXT NOT NULL,
                    next_run_at TEXT NULL,
                    payload_json TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_automations_workspace_created
                    ON automations(workspace_id, created_at DESC);

                CREATE INDEX IF NOT EXISTS ix_automations_due
                    ON automations(enabled, state, next_run_at);
                """;
            command.ExecuteNonQuery();
        }

        RecoverRunning(connection);
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

    private static void Upsert(
        SqliteConnection connection,
        PersonalAutomation automation)
    {
        var payload =
            JsonSerializer.Serialize(
                automation,
                JsonOptions);

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO automations (
                automation_id,
                workspace_id,
                created_at,
                updated_at,
                enabled,
                state,
                next_run_at,
                payload_json
            )
            VALUES (
                $id,
                $workspace,
                $created,
                $updated,
                $enabled,
                $state,
                $next,
                $payload
            )
            ON CONFLICT(automation_id) DO UPDATE SET
                workspace_id = excluded.workspace_id,
                updated_at = excluded.updated_at,
                enabled = excluded.enabled,
                state = excluded.state,
                next_run_at = excluded.next_run_at,
                payload_json = excluded.payload_json;
            """;
        command.Parameters.AddWithValue(
            "$id",
            automation.Id.ToString("D"));
        command.Parameters.AddWithValue(
            "$workspace",
            NormalizeWorkspace(
                automation.WorkspaceId));
        command.Parameters.AddWithValue(
            "$created",
            automation.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue(
            "$updated",
            automation.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue(
            "$enabled",
            automation.Enabled ? 1 : 0);
        command.Parameters.AddWithValue(
            "$state",
            automation.State);
        command.Parameters.AddWithValue(
            "$next",
            automation.NextRunAt is DateTimeOffset next
                ? next.ToString("O")
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$payload",
            payload);
        command.ExecuteNonQuery();
    }

    private void RecoverRunning(
        SqliteConnection connection)
    {
        var recovered = new List<PersonalAutomation>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT automation_id, payload_json
                FROM automations
                WHERE state = $running;
                """;
            command.Parameters.AddWithValue(
                "$running",
                AutomationStates.Running);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = Guid.TryParse(
                    reader.GetString(0),
                    out var parsed)
                    ? parsed
                    : Guid.Empty;
                var item = Deserialize(
                    reader.GetString(1),
                    id);
                if (item is not null)
                {
                    recovered.Add(item);
                }
            }
        }

        foreach (var item in recovered)
        {
            var now = DateTimeOffset.UtcNow;
            Upsert(
                connection,
                item with
                {
                    Enabled = false,
                    State = AutomationStates.Interrupted,
                    NextRunAt = null,
                    UpdatedAt = now,
                    LastRunAt = now,
                    LastRunStatus =
                        AutomationRunStatuses.Interrupted,
                    LastMessage =
                        "Automation bị gián đoạn khi ứng dụng dừng trong lúc đang chạy. Cần người dùng kiểm tra task trước khi tiếp tục."
                });
        }
    }

    private PersonalAutomation? Deserialize(
        string payload,
        Guid expectedId)
    {
        try
        {
            var item =
                JsonSerializer.Deserialize<PersonalAutomation>(
                    payload,
                    JsonOptions);
            if (item is null
                || item.Id == Guid.Empty
                || (expectedId != Guid.Empty
                    && item.Id != expectedId))
            {
                return null;
            }

            return item with
            {
                WorkspaceId =
                    NormalizeWorkspace(
                        item.WorkspaceId)
            };
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "Không đọc được automation {AutomationId}.",
                expectedId);
            return null;
        }
    }

    private static string NormalizeWorkspace(
        string value) =>
        string.IsNullOrWhiteSpace(value)
            ? PersonalWorkspaceIds.Personal
            : value.Trim().ToLowerInvariant();
}
