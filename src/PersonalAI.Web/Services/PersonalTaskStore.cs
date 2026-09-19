using System.Text.Json;
using Microsoft.Data.Sqlite;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IPersonalTaskStore
{
    PersonalTask Save(PersonalTask task);
    PersonalTask? Get(Guid taskId);
    IReadOnlyList<PersonalTask> GetAll();
}

public sealed class SqlitePersonalTaskStore : IPersonalTaskStore
{
    public const int MaximumTasks = 50;
    public const string StorageKind = "local-sqlite";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private readonly string _databasePath;
    private readonly ILogger<SqlitePersonalTaskStore> _logger;
    private readonly IWorkspaceContextAccessor _workspaceContext;
    private bool _initialized;

    public SqlitePersonalTaskStore(
        IConfiguration configuration,
        ILogger<SqlitePersonalTaskStore> logger,
        IWorkspaceContextAccessor workspaceContext)
    {
        _logger = logger;
        _workspaceContext = workspaceContext;

        var configuredRoot = configuration["Tasks:Root"]?.Trim();
        var directory = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Tasks")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredRoot));

        Directory.CreateDirectory(directory);
        _databasePath = Path.Combine(directory, "personal-tasks.db");
    }

    public PersonalTask Save(PersonalTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            var normalized = NormalizeWorkspace(task);
            Upsert(connection, normalized);
            Cleanup(connection, normalized.WorkspaceId);
            return normalized;
        }
    }

    public PersonalTask? Get(Guid taskId)
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT payload_json
                FROM personal_tasks
                WHERE task_id = $taskId
                  AND workspace_id = $workspaceId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$taskId", taskId.ToString("D"));
            command.Parameters.AddWithValue(
                "$workspaceId",
                _workspaceContext.CurrentWorkspaceId);

            var payload = command.ExecuteScalar() as string;
            return payload is null ? null : DeserializeTask(payload, taskId);
        }
    }

    public IReadOnlyList<PersonalTask> GetAll()
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            var workspaceId = _workspaceContext.CurrentWorkspaceId;
            Cleanup(connection, workspaceId);

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT task_id, payload_json
                FROM personal_tasks
                WHERE workspace_id = $workspaceId
                ORDER BY created_at DESC, rowid DESC
                LIMIT $maximumTasks;
                """;
            command.Parameters.AddWithValue("$workspaceId", workspaceId);
            command.Parameters.AddWithValue("$maximumTasks", MaximumTasks);

            using var reader = command.ExecuteReader();
            var tasks = new List<PersonalTask>();
            while (reader.Read())
            {
                var id = Guid.TryParse(reader.GetString(0), out var parsedId)
                    ? parsedId
                    : Guid.Empty;
                var task = DeserializeTask(reader.GetString(1), id);
                if (task is not null)
                {
                    tasks.Add(task);
                }
            }

            return tasks;
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
                CREATE TABLE IF NOT EXISTS personal_tasks (
                    task_id TEXT PRIMARY KEY,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    status TEXT NOT NULL,
                    workspace_id TEXT NOT NULL DEFAULT 'personal',
                    payload_json TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_personal_tasks_created_at
                    ON personal_tasks(created_at DESC);
                """;
            command.ExecuteNonQuery();
        }

        EnsureWorkspaceColumn(connection);
        RecoverInterruptedTasks(connection);
        Cleanup(connection, _workspaceContext.CurrentWorkspaceId);
        _initialized = true;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(
            $"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Shared");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void Upsert(
        SqliteConnection connection,
        PersonalTask task)
    {
        var payload = JsonSerializer.Serialize(task, JsonOptions);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO personal_tasks (
                task_id,
                created_at,
                updated_at,
                status,
                workspace_id,
                payload_json
            )
            VALUES (
                $taskId,
                $createdAt,
                $updatedAt,
                $status,
                $workspaceId,
                $payload
            )
            ON CONFLICT(task_id) DO UPDATE SET
                updated_at = excluded.updated_at,
                status = excluded.status,
                workspace_id = excluded.workspace_id,
                payload_json = excluded.payload_json;
            """;
        command.Parameters.AddWithValue("$taskId", task.Id.ToString("D"));
        command.Parameters.AddWithValue("$createdAt", task.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$status", task.Status);
        command.Parameters.AddWithValue("$workspaceId", task.WorkspaceId);
        command.Parameters.AddWithValue("$payload", payload);
        command.ExecuteNonQuery();
    }

    private void RecoverInterruptedTasks(SqliteConnection connection)
    {
        var candidates = new List<PersonalTask>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT task_id, payload_json
                FROM personal_tasks
                WHERE status = $running;
                """;
            command.Parameters.AddWithValue("$running", PersonalTaskStatuses.Running);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = Guid.TryParse(reader.GetString(0), out var parsedId)
                    ? parsedId
                    : Guid.Empty;
                var task = DeserializeTask(reader.GetString(1), id);
                if (task is not null
                    && task.Steps.Any(step =>
                        step.Status == PersonalTaskStepStatuses.Running))
                {
                    candidates.Add(task);
                }
            }
        }

        foreach (var task in candidates)
        {
            var steps = task.Steps
                .Select(step => step.Status == PersonalTaskStepStatuses.Running
                    ? step with
                    {
                        Status = PersonalTaskStepStatuses.Interrupted,
                        CompletedAt = null
                    }
                    : step)
                .ToArray();

            var recovered = task with
            {
                Status = PersonalTaskStatuses.Interrupted,
                Steps = steps,
                CompletedAt = null,
                Result = "Tác vụ bị gián đoạn khi ứng dụng dừng trong lúc một bước đang chạy. Hãy kiểm tra trạng thái thực tế trước khi khôi phục."
            };

            Upsert(connection, recovered);
        }
    }

    private PersonalTask? DeserializeTask(
        string payload,
        Guid expectedId)
    {
        try
        {
            var task = JsonSerializer.Deserialize<PersonalTask>(
                payload,
                JsonOptions);
            if (task is null
                || task.Id == Guid.Empty
                || (expectedId != Guid.Empty && task.Id != expectedId))
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(task.WorkspaceId)
                ? task with { WorkspaceId = PersonalWorkspaceIds.Personal }
                : task with { WorkspaceId = task.WorkspaceId.Trim().ToLowerInvariant() };
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "Không đọc được tác vụ đã lưu {TaskId}.",
                expectedId);
            return null;
        }
    }

    private static void Cleanup(
        SqliteConnection connection,
        string workspaceId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM personal_tasks
            WHERE rowid IN (
                SELECT rowid
                FROM personal_tasks
                WHERE workspace_id = $workspaceId
                ORDER BY created_at DESC, rowid DESC
                LIMIT -1 OFFSET $maximumTasks
            );
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$maximumTasks", MaximumTasks);
        command.ExecuteNonQuery();
    }

    private static void EnsureWorkspaceColumn(SqliteConnection connection)
    {
        var exists = false;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(personal_tasks);";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(
                    reader.GetString(1),
                    "workspace_id",
                    StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (!exists)
        {
            using var alter = connection.CreateCommand();
            alter.CommandText =
                "ALTER TABLE personal_tasks ADD COLUMN workspace_id TEXT NOT NULL DEFAULT 'personal';";
            alter.ExecuteNonQuery();
        }

        using var backfill = connection.CreateCommand();
        backfill.CommandText =
            """
            UPDATE personal_tasks
            SET workspace_id = 'personal'
            WHERE workspace_id IS NULL OR TRIM(workspace_id) = '';
            """;
        backfill.ExecuteNonQuery();

        using var index = connection.CreateCommand();
        index.CommandText =
            """
            CREATE INDEX IF NOT EXISTS ix_personal_tasks_workspace_created_at
            ON personal_tasks(workspace_id, created_at DESC);
            """;
        index.ExecuteNonQuery();
    }

    private PersonalTask NormalizeWorkspace(PersonalTask task)
    {
        var workspaceId = string.IsNullOrWhiteSpace(task.WorkspaceId)
            ? _workspaceContext.CurrentWorkspaceId
            : task.WorkspaceId.Trim().ToLowerInvariant();

        if (!string.Equals(
            workspaceId,
            _workspaceContext.CurrentWorkspaceId,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Không thể lưu tác vụ vào không gian làm việc khác với ngữ cảnh hiện tại.");
        }

        return task with { WorkspaceId = workspaceId };
    }
}
