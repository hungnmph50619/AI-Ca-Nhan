using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PersonalAI.Web.Services;

public static class ComputerOperatorCheckpointStatuses
{
    public const string Running = "running";
    public const string Interrupted = "interrupted";
    public const string Completed = "completed";
    public const string Blocked = "blocked";
}

public sealed record ComputerOperatorCheckpoint(
    Guid Id,
    string WorkspaceId,
    string Goal,
    string GoalFingerprint,
    string Status,
    IReadOnlyList<string> VerifiedMilestones,
    string CurrentSubgoal,
    double GoalProgress,
    string? LastVerifiedAction,
    string? LastVerifiedExpectedEffect,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? LastObservedStateFingerprint = null);

public interface IComputerOperatorCheckpointStore
{
    ComputerOperatorCheckpoint StartOrResume(string goal);

    ComputerOperatorCheckpoint SaveProgress(
        ComputerOperatorCheckpoint checkpoint,
        IReadOnlyCollection<string> verifiedMilestones,
        string currentSubgoal,
        double goalProgress,
        string? lastVerifiedAction = null,
        string? lastVerifiedExpectedEffect = null,
        string? lastObservedStateFingerprint = null);

    ComputerOperatorCheckpoint MarkStatus(
        ComputerOperatorCheckpoint checkpoint,
        string status);

    ComputerOperatorCheckpoint? FindResumable(string goal);

    ComputerOperatorCheckpoint? GetLatest();

    void ClearResumable(string goal);
}

public sealed class SqliteComputerOperatorCheckpointStore(
    IConfiguration configuration,
    IWorkspaceContextAccessor workspace,
    ILogger<SqliteComputerOperatorCheckpointStore> logger)
    : IComputerOperatorCheckpointStore
{
    public const int MaximumCheckpointsPerWorkspace = 20;
    public static readonly TimeSpan ResumeLifetime =
        TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly object gate = new();
    private readonly string databasePath =
        ResolveDatabasePath(configuration);
    private bool initialized;

    public ComputerOperatorCheckpoint StartOrResume(
        string goal)
    {
        var resumable = FindResumable(goal);
        if (resumable is not null)
        {
            var resumed = resumable with
            {
                Status = ComputerOperatorCheckpointStatuses.Running,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            Save(resumed);
            return resumed;
        }

        var now = DateTimeOffset.UtcNow;
        var created = new ComputerOperatorCheckpoint(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            "[Mục tiêu không lưu plaintext; đối chiếu bằng SHA-256]",
            Fingerprint(goal),
            ComputerOperatorCheckpointStatuses.Running,
            Array.Empty<string>(),
            string.Empty,
            0,
            LastVerifiedAction: null,
            LastVerifiedExpectedEffect: null,
            now,
            now,
            LastObservedStateFingerprint: null);

        Save(created);
        return created;
    }

    public ComputerOperatorCheckpoint SaveProgress(
        ComputerOperatorCheckpoint checkpoint,
        IReadOnlyCollection<string> verifiedMilestones,
        string currentSubgoal,
        double goalProgress,
        string? lastVerifiedAction = null,
        string? lastVerifiedExpectedEffect = null,
        string? lastObservedStateFingerprint = null)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(verifiedMilestones);

        EnsureWorkspace(checkpoint.WorkspaceId);

        var updated = checkpoint with
        {
            Status = ComputerOperatorCheckpointStatuses.Running,
            VerifiedMilestones = verifiedMilestones
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(SanitizeMilestone)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(64)
                .ToArray(),
            CurrentSubgoal = SanitizeCheckpointText(
                currentSubgoal,
                500,
                "[Mục tiêu con nhạy cảm đã được xác minh; nội dung không lưu]"),
            GoalProgress = Math.Clamp(
                goalProgress,
                0,
                1),
            LastVerifiedAction = SanitizeCheckpointTextNullable(
                lastVerifiedAction,
                120,
                "[Action nhạy cảm; nội dung không lưu]"),
            LastVerifiedExpectedEffect = SanitizeCheckpointTextNullable(
                lastVerifiedExpectedEffect,
                500,
                "[Kết quả nhạy cảm đã được xác minh; nội dung không lưu]"),
            LastObservedStateFingerprint =
                NormalizeStateFingerprint(
                    lastObservedStateFingerprint),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        Save(updated);
        return updated;
    }

    public ComputerOperatorCheckpoint MarkStatus(
        ComputerOperatorCheckpoint checkpoint,
        string status)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        EnsureWorkspace(checkpoint.WorkspaceId);

        var normalized = status switch
        {
            ComputerOperatorCheckpointStatuses.Running =>
                ComputerOperatorCheckpointStatuses.Running,
            ComputerOperatorCheckpointStatuses.Interrupted =>
                ComputerOperatorCheckpointStatuses.Interrupted,
            ComputerOperatorCheckpointStatuses.Completed =>
                ComputerOperatorCheckpointStatuses.Completed,
            ComputerOperatorCheckpointStatuses.Blocked =>
                ComputerOperatorCheckpointStatuses.Blocked,
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                "Trạng thái checkpoint không hợp lệ.")
        };

        var updated = checkpoint with
        {
            Status = normalized,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        Save(updated);
        return updated;
    }

    public ComputerOperatorCheckpoint? FindResumable(
        string goal)
    {
        lock (gate)
        {
            EnsureInitialized();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT payload_json
                FROM computer_operator_checkpoints
                WHERE workspace_id = $workspaceId
                  AND goal_fingerprint = $fingerprint
                  AND status IN ($running, $interrupted, $blocked)
                ORDER BY updated_at DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue(
                "$workspaceId",
                workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue(
                "$fingerprint",
                Fingerprint(goal));
            command.Parameters.AddWithValue(
                "$running",
                ComputerOperatorCheckpointStatuses.Running);
            command.Parameters.AddWithValue(
                "$interrupted",
                ComputerOperatorCheckpointStatuses.Interrupted);
            command.Parameters.AddWithValue(
                "$blocked",
                ComputerOperatorCheckpointStatuses.Blocked);

            var payload = command.ExecuteScalar() as string;
            var checkpoint = Deserialize(payload);

            if (checkpoint is null)
                return null;

            if (checkpoint.UpdatedAt + ResumeLifetime <
                DateTimeOffset.UtcNow)
            {
                return null;
            }

            return checkpoint;
        }
    }

    public ComputerOperatorCheckpoint? GetLatest()
    {
        lock (gate)
        {
            EnsureInitialized();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT payload_json
                FROM computer_operator_checkpoints
                WHERE workspace_id = $workspaceId
                ORDER BY updated_at DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue(
                "$workspaceId",
                workspace.CurrentWorkspaceId);

            return Deserialize(
                command.ExecuteScalar() as string);
        }
    }

    public void ClearResumable(
        string goal)
    {
        lock (gate)
        {
            EnsureInitialized();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                DELETE FROM computer_operator_checkpoints
                WHERE workspace_id = $workspaceId
                  AND goal_fingerprint = $fingerprint
                  AND status IN ($running, $interrupted, $blocked);
                """;
            command.Parameters.AddWithValue(
                "$workspaceId",
                workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue(
                "$fingerprint",
                Fingerprint(goal));
            command.Parameters.AddWithValue(
                "$running",
                ComputerOperatorCheckpointStatuses.Running);
            command.Parameters.AddWithValue(
                "$interrupted",
                ComputerOperatorCheckpointStatuses.Interrupted);
            command.Parameters.AddWithValue(
                "$blocked",
                ComputerOperatorCheckpointStatuses.Blocked);
            command.ExecuteNonQuery();
        }
    }

    private void Save(
        ComputerOperatorCheckpoint checkpoint)
    {
        EnsureWorkspace(checkpoint.WorkspaceId);

        lock (gate)
        {
            EnsureInitialized();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO computer_operator_checkpoints (
                    checkpoint_id,
                    workspace_id,
                    goal_fingerprint,
                    status,
                    created_at,
                    updated_at,
                    payload_json
                )
                VALUES (
                    $id,
                    $workspaceId,
                    $fingerprint,
                    $status,
                    $createdAt,
                    $updatedAt,
                    $payload
                )
                ON CONFLICT(checkpoint_id) DO UPDATE SET
                    status = excluded.status,
                    updated_at = excluded.updated_at,
                    payload_json = excluded.payload_json;
                """;
            command.Parameters.AddWithValue(
                "$id",
                checkpoint.Id.ToString("D"));
            command.Parameters.AddWithValue(
                "$workspaceId",
                checkpoint.WorkspaceId);
            command.Parameters.AddWithValue(
                "$fingerprint",
                checkpoint.GoalFingerprint);
            command.Parameters.AddWithValue(
                "$status",
                checkpoint.Status);
            command.Parameters.AddWithValue(
                "$createdAt",
                checkpoint.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue(
                "$updatedAt",
                checkpoint.UpdatedAt.ToString("O"));
            command.Parameters.AddWithValue(
                "$payload",
                JsonSerializer.Serialize(
                    checkpoint,
                    JsonOptions));
            command.ExecuteNonQuery();

            Cleanup(
                connection,
                checkpoint.WorkspaceId);
        }
    }

    private void EnsureInitialized()
    {
        if (initialized)
            return;

        Directory.CreateDirectory(
            Path.GetDirectoryName(databasePath)!);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS computer_operator_checkpoints (
                checkpoint_id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                goal_fingerprint TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                payload_json TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_computer_operator_checkpoint_lookup
            ON computer_operator_checkpoints(
                workspace_id,
                goal_fingerprint,
                updated_at DESC
            );
            """;
        command.ExecuteNonQuery();

        RecoverInterrupted(
            connection);

        initialized = true;
    }

    private void RecoverInterrupted(
        SqliteConnection connection)
    {
        using var select = connection.CreateCommand();
        select.CommandText =
            """
            SELECT checkpoint_id, payload_json
            FROM computer_operator_checkpoints
            WHERE workspace_id = $workspaceId
              AND status = $running;
            """;
        select.Parameters.AddWithValue(
            "$workspaceId",
            workspace.CurrentWorkspaceId);
        select.Parameters.AddWithValue(
            "$running",
            ComputerOperatorCheckpointStatuses.Running);

        var recovered = new List<ComputerOperatorCheckpoint>();
        using (var reader = select.ExecuteReader())
        {
            while (reader.Read())
            {
                var checkpoint = Deserialize(
                    reader.GetString(1));

                if (checkpoint is not null)
                {
                    recovered.Add(
                        checkpoint with
                        {
                            Status =
                                ComputerOperatorCheckpointStatuses.Interrupted,
                            UpdatedAt =
                                DateTimeOffset.UtcNow
                        });
                }
            }
        }

        foreach (var checkpoint in recovered)
        {
            using var update = connection.CreateCommand();
            update.CommandText =
                """
                UPDATE computer_operator_checkpoints
                SET status = $status,
                    updated_at = $updatedAt,
                    payload_json = $payload
                WHERE checkpoint_id = $id;
                """;
            update.Parameters.AddWithValue(
                "$status",
                checkpoint.Status);
            update.Parameters.AddWithValue(
                "$updatedAt",
                checkpoint.UpdatedAt.ToString("O"));
            update.Parameters.AddWithValue(
                "$payload",
                JsonSerializer.Serialize(
                    checkpoint,
                    JsonOptions));
            update.Parameters.AddWithValue(
                "$id",
                checkpoint.Id.ToString("D"));
            update.ExecuteNonQuery();
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(
            $"Data Source={databasePath};Mode=ReadWriteCreate;Cache=Shared");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();

        return connection;
    }

    private static void Cleanup(
        SqliteConnection connection,
        string workspaceId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM computer_operator_checkpoints
            WHERE rowid IN (
                SELECT rowid
                FROM computer_operator_checkpoints
                WHERE workspace_id = $workspaceId
                ORDER BY updated_at DESC, rowid DESC
                LIMIT -1 OFFSET $maximum
            );
            """;
        command.Parameters.AddWithValue(
            "$workspaceId",
            workspaceId);
        command.Parameters.AddWithValue(
            "$maximum",
            MaximumCheckpointsPerWorkspace);
        command.ExecuteNonQuery();
    }

    private ComputerOperatorCheckpoint? Deserialize(
        string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            var checkpoint =
                JsonSerializer.Deserialize<ComputerOperatorCheckpoint>(
                    payload,
                    JsonOptions);

            if (checkpoint is null ||
                checkpoint.Id == Guid.Empty ||
                !string.Equals(
                    checkpoint.WorkspaceId,
                    workspace.CurrentWorkspaceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return checkpoint;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Không đọc được Computer Operator checkpoint.");
            return null;
        }
    }

    private void EnsureWorkspace(
        string workspaceId)
    {
        if (!string.Equals(
                workspaceId,
                workspace.CurrentWorkspaceId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Checkpoint không thuộc workspace hiện tại.");
        }
    }

    private static string ResolveDatabasePath(
        IConfiguration configuration)
    {
        var configured =
            configuration["Tasks:Root"]?.Trim();

        var directory =
            string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "PersonalAI",
                    "Tasks")
                : Path.GetFullPath(
                    Environment.ExpandEnvironmentVariables(
                        configured));

        return Path.Combine(
            directory,
            "computer-operator-checkpoints.db");
    }

    private static string NormalizeGoal(
        string goal) =>
        (goal ?? string.Empty).Trim();

    private static string Fingerprint(
        string goal)
    {
        var normalized =
            NormalizeGoal(goal)
                .ToLowerInvariant();

        var bytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexString(bytes);
    }

    private static string? NormalizeStateFingerprint(
        string? value)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

        if (normalized.Length == 0)
            return null;

        if (normalized.Length != 64 ||
            normalized.Any(character =>
                !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "State fingerprint checkpoint phải là SHA-256 hex 64 ký tự.",
                nameof(value));
        }

        return normalized;
    }

    private static readonly string[] SensitiveTerms =
    [
        "password",
        "mật khẩu",
        "otp",
        "2fa",
        "mã xác thực",
        "verification code",
        "api key",
        "secret",
        "access token",
        "refresh token",
        "private key",
        "bearer "
    ];

    private static string SanitizeMilestone(
        string value) =>
        SanitizeCheckpointText(
            value,
            500,
            "[Mốc nhạy cảm đã được xác minh; nội dung không lưu]");

    private static string SanitizeCheckpointText(
        string? value,
        int maximum,
        string redacted)
    {
        var text =
            Limit(
                value,
                maximum);

        return SensitiveTerms.Any(term =>
                text.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase))
            ? redacted
            : text;
    }

    private static string? SanitizeCheckpointTextNullable(
        string? value,
        int maximum,
        string redacted)
    {
        var text =
            SanitizeCheckpointText(
                value,
                maximum,
                redacted);

        return text.Length == 0
            ? null
            : text;
    }

    private static string Limit(
        string? value,
        int maximum)
    {
        var normalized =
            (value ?? string.Empty).Trim();

        return normalized.Length <= maximum
            ? normalized
            : normalized[..maximum];
    }

    private static string? LimitNullable(
        string? value,
        int maximum)
    {
        var normalized = Limit(value, maximum);
        return normalized.Length == 0
            ? null
            : normalized;
    }
}
