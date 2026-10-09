using Microsoft.Data.Sqlite;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorExperiencePattern(
    string ContextFingerprint,
    string FailureCode,
    string StrategyKey,
    int SampleCount,
    double AverageConfidence,
    long AverageDurationMilliseconds,
    DateTimeOffset LastVerifiedAt);

public sealed record ComputerOperatorPersistentTimingProfile(
    string Stage,
    string Action,
    int SampleCount,
    double MedianMilliseconds,
    double P90Milliseconds,
    double P95Milliseconds,
    double MaximumMilliseconds,
    double Confidence,
    DateTimeOffset UpdatedAtUtc);

public interface IComputerOperatorExperienceAggregateStore
{
    void UpsertPattern(ComputerOperatorExperiencePattern pattern);

    IReadOnlyList<ComputerOperatorExperiencePattern> GetPatterns(
        int limit = 100);

    void UpsertTimingProfile(
        ComputerOperatorPersistentTimingProfile profile);

    ComputerOperatorPersistentTimingProfile? GetTimingProfile(
        string stage,
        string action);

    IReadOnlyList<ComputerOperatorPersistentTimingProfile> GetTimingProfiles();
}

public sealed class SqliteComputerOperatorExperienceAggregateStore(
    IConfiguration configuration,
    IWorkspaceContextAccessor workspace)
    : IComputerOperatorExperienceAggregateStore
{
    private readonly object gate = new();
    private readonly string databasePath =
        ResolveDatabasePath(configuration);
    private bool initialized;

    public void UpsertPattern(
        ComputerOperatorExperiencePattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        lock (gate)
        {
            EnsureInitialized();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO computer_operator_experience_patterns (
                    workspace_id,
                    context_fingerprint,
                    failure_code,
                    strategy_key,
                    sample_count,
                    average_confidence,
                    average_duration_ms,
                    last_verified_at
                )
                VALUES (
                    $workspaceId,
                    $context,
                    $failure,
                    $strategy,
                    $samples,
                    $confidence,
                    $duration,
                    $verifiedAt
                )
                ON CONFLICT(
                    workspace_id,
                    context_fingerprint,
                    failure_code,
                    strategy_key
                )
                DO UPDATE SET
                    sample_count = excluded.sample_count,
                    average_confidence = excluded.average_confidence,
                    average_duration_ms = excluded.average_duration_ms,
                    last_verified_at = excluded.last_verified_at;
                """;

            command.Parameters.AddWithValue("$workspaceId", workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue("$context", pattern.ContextFingerprint);
            command.Parameters.AddWithValue("$failure", pattern.FailureCode);
            command.Parameters.AddWithValue("$strategy", pattern.StrategyKey);
            command.Parameters.AddWithValue("$samples", Math.Max(0, pattern.SampleCount));
            command.Parameters.AddWithValue("$confidence", Math.Clamp(pattern.AverageConfidence, 0, 1));
            command.Parameters.AddWithValue("$duration", Math.Max(0, pattern.AverageDurationMilliseconds));
            command.Parameters.AddWithValue("$verifiedAt", pattern.LastVerifiedAt.ToString("O"));

            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<ComputerOperatorExperiencePattern> GetPatterns(
        int limit = 100)
    {
        var resolvedLimit = Math.Clamp(limit, 1, 500);

        lock (gate)
        {
            EnsureInitialized();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                SELECT
                    context_fingerprint,
                    failure_code,
                    strategy_key,
                    sample_count,
                    average_confidence,
                    average_duration_ms,
                    last_verified_at
                FROM computer_operator_experience_patterns
                WHERE workspace_id = $workspaceId
                ORDER BY last_verified_at DESC
                LIMIT $limit;
                """;

            command.Parameters.AddWithValue("$workspaceId", workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue("$limit", resolvedLimit);

            var result = new List<ComputerOperatorExperiencePattern>();

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!DateTimeOffset.TryParse(
                        reader.GetString(6),
                        out var lastVerifiedAt))
                {
                    continue;
                }

                result.Add(
                    new(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetInt32(3),
                        reader.GetDouble(4),
                        reader.GetInt64(5),
                        lastVerifiedAt));
            }

            return result;
        }
    }

    public void UpsertTimingProfile(
        ComputerOperatorPersistentTimingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        lock (gate)
        {
            EnsureInitialized();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO computer_operator_timing_profiles (
                    workspace_id,
                    stage,
                    action,
                    sample_count,
                    median_ms,
                    p90_ms,
                    p95_ms,
                    maximum_ms,
                    confidence,
                    updated_at
                )
                VALUES (
                    $workspaceId,
                    $stage,
                    $action,
                    $samples,
                    $median,
                    $p90,
                    $p95,
                    $maximum,
                    $confidence,
                    $updatedAt
                )
                ON CONFLICT(workspace_id, stage, action)
                DO UPDATE SET
                    sample_count = excluded.sample_count,
                    median_ms = excluded.median_ms,
                    p90_ms = excluded.p90_ms,
                    p95_ms = excluded.p95_ms,
                    maximum_ms = excluded.maximum_ms,
                    confidence = excluded.confidence,
                    updated_at = excluded.updated_at;
                """;

            command.Parameters.AddWithValue("$workspaceId", workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue("$stage", Normalize(profile.Stage));
            command.Parameters.AddWithValue("$action", Normalize(profile.Action));
            command.Parameters.AddWithValue("$samples", Math.Max(0, profile.SampleCount));
            command.Parameters.AddWithValue("$median", Math.Max(0, profile.MedianMilliseconds));
            command.Parameters.AddWithValue("$p90", Math.Max(0, profile.P90Milliseconds));
            command.Parameters.AddWithValue("$p95", Math.Max(0, profile.P95Milliseconds));
            command.Parameters.AddWithValue("$maximum", Math.Max(0, profile.MaximumMilliseconds));
            command.Parameters.AddWithValue("$confidence", Math.Clamp(profile.Confidence, 0, 1));
            command.Parameters.AddWithValue("$updatedAt", profile.UpdatedAtUtc.ToString("O"));

            command.ExecuteNonQuery();
        }
    }

    public ComputerOperatorPersistentTimingProfile? GetTimingProfile(
        string stage,
        string action)
    {
        lock (gate)
        {
            EnsureInitialized();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                SELECT
                    stage,
                    action,
                    sample_count,
                    median_ms,
                    p90_ms,
                    p95_ms,
                    maximum_ms,
                    confidence,
                    updated_at
                FROM computer_operator_timing_profiles
                WHERE workspace_id = $workspaceId
                  AND stage = $stage
                  AND action = $action
                LIMIT 1;
                """;

            command.Parameters.AddWithValue("$workspaceId", workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue("$stage", Normalize(stage));
            command.Parameters.AddWithValue("$action", Normalize(action));

            using var reader = command.ExecuteReader();
            return reader.Read()
                ? ReadTimingProfile(reader)
                : null;
        }
    }

    public IReadOnlyList<ComputerOperatorPersistentTimingProfile> GetTimingProfiles()
    {
        lock (gate)
        {
            EnsureInitialized();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                SELECT
                    stage,
                    action,
                    sample_count,
                    median_ms,
                    p90_ms,
                    p95_ms,
                    maximum_ms,
                    confidence,
                    updated_at
                FROM computer_operator_timing_profiles
                WHERE workspace_id = $workspaceId
                ORDER BY sample_count DESC, stage, action;
                """;

            command.Parameters.AddWithValue("$workspaceId", workspace.CurrentWorkspaceId);

            var result = new List<ComputerOperatorPersistentTimingProfile>();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var item = ReadTimingProfile(reader);
                if (item is not null)
                    result.Add(item);
            }

            return result;
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
            CREATE TABLE IF NOT EXISTS computer_operator_experience_patterns (
                workspace_id TEXT NOT NULL,
                context_fingerprint TEXT NOT NULL,
                failure_code TEXT NOT NULL,
                strategy_key TEXT NOT NULL,
                sample_count INTEGER NOT NULL,
                average_confidence REAL NOT NULL,
                average_duration_ms INTEGER NOT NULL,
                last_verified_at TEXT NOT NULL,
                PRIMARY KEY (
                    workspace_id,
                    context_fingerprint,
                    failure_code,
                    strategy_key
                )
            );

            CREATE INDEX IF NOT EXISTS ix_computer_operator_patterns_verified
            ON computer_operator_experience_patterns(
                workspace_id,
                last_verified_at DESC
            );

            CREATE TABLE IF NOT EXISTS computer_operator_timing_profiles (
                workspace_id TEXT NOT NULL,
                stage TEXT NOT NULL,
                action TEXT NOT NULL,
                sample_count INTEGER NOT NULL,
                median_ms REAL NOT NULL,
                p90_ms REAL NOT NULL,
                p95_ms REAL NOT NULL,
                maximum_ms REAL NOT NULL,
                confidence REAL NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (
                    workspace_id,
                    stage,
                    action
                )
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

        using var command = connection.CreateCommand();
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

    private static ComputerOperatorPersistentTimingProfile? ReadTimingProfile(
        SqliteDataReader reader)
    {
        if (!DateTimeOffset.TryParse(
                reader.GetString(8),
                out var updatedAt))
        {
            return null;
        }

        return new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetDouble(3),
            reader.GetDouble(4),
            reader.GetDouble(5),
            reader.GetDouble(6),
            reader.GetDouble(7),
            updatedAt);
    }

    private static string Normalize(
        string? value)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        return normalized.Length == 0
            ? "none"
            : normalized;
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
            "computer-operator-experience.db");
    }
}
