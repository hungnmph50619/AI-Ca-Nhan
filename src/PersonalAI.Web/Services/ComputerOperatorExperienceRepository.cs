using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace PersonalAI.Web.Services;

public static class ComputerOperatorExperienceKinds
{
    public const string Failure = "failure";
    public const string Recovery = "recovery";
    public const string Experience = "experience";
    public const string Candidate = "candidate";
    public const string Strategy = "strategy";
}

public sealed record ComputerOperatorExperienceEvent(
    Guid Id,
    string WorkspaceId,
    string Kind,
    string ContextFingerprint,
    string FailureCode,
    string StrategyKey,
    string Outcome,
    long DurationMilliseconds,
    double Confidence,
    bool Verified,
    DateTimeOffset CreatedAt);

public sealed record ComputerOperatorExperienceStoreDiagnostics(
    int SchemaVersion,
    bool UsesWal,
    string Provider,
    int EventCount,
    int VerifiedEventCount,
    int FailureCount,
    int RecoveryCount,
    int CandidateCount,
    int ExperienceCount,
    int StrategyCount);

public interface IComputerOperatorExperienceRepository
{
    const int MaximumValidationScan = 500;

    ComputerOperatorExperienceEvent Append(
        string kind,
        string contextFingerprint,
        string? failureCode = null,
        string? strategyKey = null,
        string? outcome = null,
        long durationMilliseconds = 0,
        double confidence = 0,
        bool verified = false);

    IReadOnlyList<ComputerOperatorExperienceEvent> GetRecent(
        int limit = 100);

    ComputerOperatorExperienceStoreDiagnostics GetDiagnostics();

    string FingerprintContext(string context);
}

/// <summary>
/// Kho dữ liệu nền cho Computer Operator experience.
/// Chỉ lưu fingerprint + mã kỹ thuật giới hạn, không lưu raw goal/text/screenshot.
/// </summary>
public sealed class SqliteComputerOperatorExperienceRepository(
    IConfiguration configuration,
    IWorkspaceContextAccessor workspace)
    : IComputerOperatorExperienceRepository
{
    public const int SchemaVersion = 1;
    public const int MaximumEventsPerWorkspace = 5000;
    public const int MaximumQueryLimit = 500;

    private readonly object gate = new();
    private readonly string databasePath =
        ResolveDatabasePath(configuration);
    private bool initialized;

    public ComputerOperatorExperienceEvent Append(
        string kind,
        string contextFingerprint,
        string? failureCode = null,
        string? strategyKey = null,
        string? outcome = null,
        long durationMilliseconds = 0,
        double confidence = 0,
        bool verified = false)
    {
        var normalizedKind =
            NormalizeKind(kind);

        var fingerprint =
            NormalizeFingerprint(
                contextFingerprint);

        var created =
            new ComputerOperatorExperienceEvent(
                Guid.NewGuid(),
                workspace.CurrentWorkspaceId,
                normalizedKind,
                fingerprint,
                NormalizeDimension(
                    failureCode,
                    80),
                NormalizeDimension(
                    strategyKey,
                    120),
                NormalizeDimension(
                    outcome,
                    80),
                Math.Max(
                    0,
                    durationMilliseconds),
                Math.Clamp(
                    confidence,
                    0,
                    1),
                verified,
                DateTimeOffset.UtcNow);

        lock (gate)
        {
            EnsureInitialized();

            using var connection =
                OpenConnection();

            using var transaction =
                connection.BeginTransaction();

            using (var command =
                connection.CreateCommand())
            {
                command.Transaction =
                    transaction;

                command.CommandText =
                    """
                    INSERT INTO computer_operator_experience_events (
                        event_id,
                        workspace_id,
                        kind,
                        context_fingerprint,
                        failure_code,
                        strategy_key,
                        outcome,
                        duration_ms,
                        confidence,
                        verified,
                        created_at
                    )
                    VALUES (
                        $id,
                        $workspaceId,
                        $kind,
                        $fingerprint,
                        $failureCode,
                        $strategyKey,
                        $outcome,
                        $durationMs,
                        $confidence,
                        $verified,
                        $createdAt
                    );
                    """;

                command.Parameters.AddWithValue(
                    "$id",
                    created.Id.ToString("D"));
                command.Parameters.AddWithValue(
                    "$workspaceId",
                    created.WorkspaceId);
                command.Parameters.AddWithValue(
                    "$kind",
                    created.Kind);
                command.Parameters.AddWithValue(
                    "$fingerprint",
                    created.ContextFingerprint);
                command.Parameters.AddWithValue(
                    "$failureCode",
                    created.FailureCode);
                command.Parameters.AddWithValue(
                    "$strategyKey",
                    created.StrategyKey);
                command.Parameters.AddWithValue(
                    "$outcome",
                    created.Outcome);
                command.Parameters.AddWithValue(
                    "$durationMs",
                    created.DurationMilliseconds);
                command.Parameters.AddWithValue(
                    "$confidence",
                    created.Confidence);
                command.Parameters.AddWithValue(
                    "$verified",
                    created.Verified ? 1 : 0);
                command.Parameters.AddWithValue(
                    "$createdAt",
                    created.CreatedAt.ToString("O"));

                command.ExecuteNonQuery();
            }

            Cleanup(
                connection,
                transaction);

            transaction.Commit();
        }

        return created;
    }

    public IReadOnlyList<ComputerOperatorExperienceEvent> GetRecent(
        int limit = 100)
    {
        var resolvedLimit =
            Math.Clamp(
                limit,
                1,
                MaximumQueryLimit);

        lock (gate)
        {
            EnsureInitialized();

            using var connection =
                OpenConnection();
            using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                SELECT
                    event_id,
                    workspace_id,
                    kind,
                    context_fingerprint,
                    failure_code,
                    strategy_key,
                    outcome,
                    duration_ms,
                    confidence,
                    verified,
                    created_at
                FROM computer_operator_experience_events
                WHERE workspace_id = $workspaceId
                ORDER BY created_at DESC, rowid DESC
                LIMIT $limit;
                """;

            command.Parameters.AddWithValue(
                "$workspaceId",
                workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue(
                "$limit",
                resolvedLimit);

            var result =
                new List<ComputerOperatorExperienceEvent>();

            using var reader =
                command.ExecuteReader();

            while (reader.Read())
            {
                if (!Guid.TryParse(
                        reader.GetString(0),
                        out var id) ||
                    !DateTimeOffset.TryParse(
                        reader.GetString(10),
                        out var createdAt))
                {
                    continue;
                }

                result.Add(
                    new(
                        id,
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetString(5),
                        reader.GetString(6),
                        reader.GetInt64(7),
                        reader.GetDouble(8),
                        reader.GetInt64(9) != 0,
                        createdAt));
            }

            return result;
        }
    }

    public ComputerOperatorExperienceStoreDiagnostics GetDiagnostics()
    {
        lock (gate)
        {
            EnsureInitialized();

            using var connection =
                OpenConnection();
            using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                SELECT
                    COUNT(*),
                    SUM(CASE WHEN verified = 1 THEN 1 ELSE 0 END),
                    SUM(CASE WHEN kind = $failure THEN 1 ELSE 0 END),
                    SUM(CASE WHEN kind = $recovery THEN 1 ELSE 0 END),
                    SUM(CASE WHEN kind = $candidate THEN 1 ELSE 0 END),
                    SUM(CASE WHEN kind = $experience THEN 1 ELSE 0 END),
                    SUM(CASE WHEN kind = $strategy THEN 1 ELSE 0 END)
                FROM computer_operator_experience_events
                WHERE workspace_id = $workspaceId;
                """;

            command.Parameters.AddWithValue(
                "$workspaceId",
                workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue(
                "$failure",
                ComputerOperatorExperienceKinds.Failure);
            command.Parameters.AddWithValue(
                "$recovery",
                ComputerOperatorExperienceKinds.Recovery);
            command.Parameters.AddWithValue(
                "$candidate",
                ComputerOperatorExperienceKinds.Candidate);
            command.Parameters.AddWithValue(
                "$experience",
                ComputerOperatorExperienceKinds.Experience);
            command.Parameters.AddWithValue(
                "$strategy",
                ComputerOperatorExperienceKinds.Strategy);

            using var reader =
                command.ExecuteReader();

            if (!reader.Read())
            {
                return new(
                    SchemaVersion,
                    UsesWal: true,
                    "sqlite",
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0);
            }

            return new(
                SchemaVersion,
                UsesWal: true,
                "sqlite",
                ReadCount(reader, 0),
                ReadCount(reader, 1),
                ReadCount(reader, 2),
                ReadCount(reader, 3),
                ReadCount(reader, 4),
                ReadCount(reader, 5),
                ReadCount(reader, 6));
        }
    }

    public string FingerprintContext(
        string context)
    {
        var normalized =
            (context ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                "Context dùng để tạo fingerprint không được trống.",
                nameof(context));
        }

        var bytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    normalized));

        return Convert.ToHexString(
            bytes);
    }

    private void EnsureInitialized()
    {
        if (initialized)
            return;

        Directory.CreateDirectory(
            Path.GetDirectoryName(
                databasePath)!);

        using var connection =
            OpenConnection();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS computer_operator_experience_schema (
                schema_version INTEGER NOT NULL
            );

            INSERT INTO computer_operator_experience_schema(schema_version)
            SELECT 1
            WHERE NOT EXISTS (
                SELECT 1
                FROM computer_operator_experience_schema
            );

            CREATE TABLE IF NOT EXISTS computer_operator_experience_events (
                event_id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                kind TEXT NOT NULL,
                context_fingerprint TEXT NOT NULL,
                failure_code TEXT NOT NULL,
                strategy_key TEXT NOT NULL,
                outcome TEXT NOT NULL,
                duration_ms INTEGER NOT NULL,
                confidence REAL NOT NULL,
                verified INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_computer_operator_experience_context
            ON computer_operator_experience_events(
                workspace_id,
                context_fingerprint,
                kind,
                created_at DESC
            );

            CREATE INDEX IF NOT EXISTS ix_computer_operator_experience_strategy
            ON computer_operator_experience_events(
                workspace_id,
                strategy_key,
                verified,
                created_at DESC
            );
            """;

        command.ExecuteNonQuery();

        initialized = true;
    }

    private SqliteConnection OpenConnection()
    {
        var connection =
            new SqliteConnection(
                $"Data Source={databasePath};Mode=ReadWriteCreate;Cache=Shared");

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            PRAGMA busy_timeout=5000;
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA foreign_keys=ON;
            """;

        command.ExecuteNonQuery();

        return connection;
    }

    private void Cleanup(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            """
            DELETE FROM computer_operator_experience_events
            WHERE rowid IN (
                SELECT rowid
                FROM computer_operator_experience_events
                WHERE workspace_id = $workspaceId
                ORDER BY created_at DESC, rowid DESC
                LIMIT -1 OFFSET $maximum
            );
            """;

        command.Parameters.AddWithValue(
            "$workspaceId",
            workspace.CurrentWorkspaceId);
        command.Parameters.AddWithValue(
            "$maximum",
            MaximumEventsPerWorkspace);

        command.ExecuteNonQuery();
    }

    private static string ResolveDatabasePath(
        IConfiguration configuration)
    {
        var configured =
            configuration["Tasks:Root"]?.Trim();

        var directory =
            string.IsNullOrWhiteSpace(
                configured)
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
            "computer-operator-experience.db");
    }

    private static string NormalizeKind(
        string kind)
    {
        var normalized =
            (kind ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        return normalized switch
        {
            ComputerOperatorExperienceKinds.Failure =>
                ComputerOperatorExperienceKinds.Failure,
            ComputerOperatorExperienceKinds.Recovery =>
                ComputerOperatorExperienceKinds.Recovery,
            ComputerOperatorExperienceKinds.Experience =>
                ComputerOperatorExperienceKinds.Experience,
            ComputerOperatorExperienceKinds.Candidate =>
                ComputerOperatorExperienceKinds.Candidate,
            ComputerOperatorExperienceKinds.Strategy =>
                ComputerOperatorExperienceKinds.Strategy,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                "Loại experience event không hợp lệ.")
        };
    }

    private static string NormalizeFingerprint(
        string value)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

        if (normalized.Length != 64 ||
            normalized.Any(character =>
                !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Context fingerprint phải là SHA-256 hex 64 ký tự.",
                nameof(value));
        }

        return normalized;
    }

    private static string NormalizeDimension(
        string? value,
        int maximum)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        if (normalized.Length > maximum)
        {
            normalized =
                normalized[..maximum];
        }

        return normalized;
    }

    private static int ReadCount(
        SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? 0
            : Convert.ToInt32(
                reader.GetValue(ordinal));
}
