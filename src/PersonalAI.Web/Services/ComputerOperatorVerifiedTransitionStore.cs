using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorVerifiedTransition(
    string FromStateFingerprint,
    string StrategyKey,
    string ExpectedEffectFingerprint,
    int SuccessCount,
    double AverageConfidence,
    long AverageDurationMilliseconds,
    DateTimeOffset FirstVerifiedAt,
    DateTimeOffset LastVerifiedAt);

public interface IComputerOperatorVerifiedTransitionStore
{
    ComputerOperatorVerifiedTransition RecordVerified(
        string fromStateFingerprint,
        string strategyKey,
        string expectedEffect,
        double confidence,
        long durationMilliseconds = 0);

    IReadOnlyList<ComputerOperatorVerifiedTransition> FindExact(
        string fromStateFingerprint,
        int limit = 20);

    IReadOnlyList<ComputerOperatorVerifiedTransition> GetRecent(
        int limit = 100);

    string FingerprintSemanticState(
        string value);
}

/// <summary>
/// Bộ nhớ transition đã được VERIFY PASS.
/// Không lưu raw goal/expected-effect; chỉ lưu SHA-256 fingerprint + strategy key kỹ thuật.
/// Một failure ở bước sau không xóa các transition đã xác minh trước đó.
/// </summary>
public sealed class SqliteComputerOperatorVerifiedTransitionStore(
    IConfiguration configuration,
    IWorkspaceContextAccessor workspace)
    : IComputerOperatorVerifiedTransitionStore
{
    public const int MaximumQueryLimit = 200;

    private readonly object gate = new();
    private readonly string databasePath =
        ResolveDatabasePath(configuration);
    private bool initialized;

    public ComputerOperatorVerifiedTransition RecordVerified(
        string fromStateFingerprint,
        string strategyKey,
        string expectedEffect,
        double confidence,
        long durationMilliseconds = 0)
    {
        var from =
            NormalizeFingerprint(
                fromStateFingerprint);

        var strategy =
            NormalizeDimension(
                strategyKey,
                120);

        if (strategy.Length == 0)
        {
            throw new ArgumentException(
                "Strategy key không được trống.",
                nameof(strategyKey));
        }

        var effect =
            FingerprintSemanticState(
                expectedEffect);

        var now =
            DateTimeOffset.UtcNow;

        lock (gate)
        {
            EnsureInitialized();

            using var connection =
                OpenConnection();

            using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO computer_operator_verified_transitions (
                    workspace_id,
                    from_state_fingerprint,
                    strategy_key,
                    expected_effect_fingerprint,
                    success_count,
                    confidence_total,
                    duration_total_ms,
                    first_verified_at,
                    last_verified_at
                )
                VALUES (
                    $workspaceId,
                    $from,
                    $strategy,
                    $effect,
                    1,
                    $confidence,
                    $duration,
                    $now,
                    $now
                )
                ON CONFLICT(
                    workspace_id,
                    from_state_fingerprint,
                    strategy_key,
                    expected_effect_fingerprint
                )
                DO UPDATE SET
                    success_count = success_count + 1,
                    confidence_total = confidence_total + excluded.confidence_total,
                    duration_total_ms = duration_total_ms + excluded.duration_total_ms,
                    last_verified_at = excluded.last_verified_at;
                """;

            command.Parameters.AddWithValue(
                "$workspaceId",
                workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue(
                "$from",
                from);
            command.Parameters.AddWithValue(
                "$strategy",
                strategy);
            command.Parameters.AddWithValue(
                "$effect",
                effect);
            command.Parameters.AddWithValue(
                "$confidence",
                Math.Clamp(
                    confidence,
                    0,
                    1));
            command.Parameters.AddWithValue(
                "$duration",
                Math.Max(
                    0,
                    durationMilliseconds));
            command.Parameters.AddWithValue(
                "$now",
                now.ToString("O"));

            command.ExecuteNonQuery();

            return ReadOne(
                connection,
                from,
                strategy,
                effect)
                ?? throw new InvalidOperationException(
                    "Không đọc lại được verified transition vừa ghi.");
        }
    }

    public IReadOnlyList<ComputerOperatorVerifiedTransition> FindExact(
        string fromStateFingerprint,
        int limit = 20)
    {
        var from =
            NormalizeFingerprint(
                fromStateFingerprint);

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
                    from_state_fingerprint,
                    strategy_key,
                    expected_effect_fingerprint,
                    success_count,
                    confidence_total,
                    duration_total_ms,
                    first_verified_at,
                    last_verified_at
                FROM computer_operator_verified_transitions
                WHERE workspace_id = $workspaceId
                  AND from_state_fingerprint = $from
                ORDER BY
                    success_count DESC,
                    (confidence_total / success_count) DESC,
                    last_verified_at DESC
                LIMIT $limit;
                """;

            command.Parameters.AddWithValue(
                "$workspaceId",
                workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue(
                "$from",
                from);
            command.Parameters.AddWithValue(
                "$limit",
                resolvedLimit);

            return ReadMany(
                command);
        }
    }

    public IReadOnlyList<ComputerOperatorVerifiedTransition> GetRecent(
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
                    from_state_fingerprint,
                    strategy_key,
                    expected_effect_fingerprint,
                    success_count,
                    confidence_total,
                    duration_total_ms,
                    first_verified_at,
                    last_verified_at
                FROM computer_operator_verified_transitions
                WHERE workspace_id = $workspaceId
                ORDER BY last_verified_at DESC
                LIMIT $limit;
                """;

            command.Parameters.AddWithValue(
                "$workspaceId",
                workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue(
                "$limit",
                resolvedLimit);

            return ReadMany(
                command);
        }
    }

    public string FingerprintSemanticState(
        string value)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                "Semantic state không được trống.",
                nameof(value));
        }

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    normalized)));
    }

    private ComputerOperatorVerifiedTransition? ReadOne(
        SqliteConnection connection,
        string from,
        string strategy,
        string effect)
    {
        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                from_state_fingerprint,
                strategy_key,
                expected_effect_fingerprint,
                success_count,
                confidence_total,
                duration_total_ms,
                first_verified_at,
                last_verified_at
            FROM computer_operator_verified_transitions
            WHERE workspace_id = $workspaceId
              AND from_state_fingerprint = $from
              AND strategy_key = $strategy
              AND expected_effect_fingerprint = $effect
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$workspaceId",
            workspace.CurrentWorkspaceId);
        command.Parameters.AddWithValue(
            "$from",
            from);
        command.Parameters.AddWithValue(
            "$strategy",
            strategy);
        command.Parameters.AddWithValue(
            "$effect",
            effect);

        using var reader =
            command.ExecuteReader();

        return reader.Read()
            ? ReadCurrent(reader)
            : null;
    }

    private static IReadOnlyList<ComputerOperatorVerifiedTransition> ReadMany(
        SqliteCommand command)
    {
        var result =
            new List<ComputerOperatorVerifiedTransition>();

        using var reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            var item =
                ReadCurrent(reader);

            if (item is not null)
                result.Add(item);
        }

        return result;
    }

    private static ComputerOperatorVerifiedTransition? ReadCurrent(
        SqliteDataReader reader)
    {
        if (!DateTimeOffset.TryParse(
                reader.GetString(6),
                out var firstVerifiedAt) ||
            !DateTimeOffset.TryParse(
                reader.GetString(7),
                out var lastVerifiedAt))
        {
            return null;
        }

        var count =
            Math.Max(
                1,
                reader.GetInt32(3));

        return new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            count,
            reader.GetDouble(4) /
                count,
            (long)Math.Round(
                (double)reader.GetInt64(5) /
                count),
            firstVerifiedAt,
            lastVerifiedAt);
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
            CREATE TABLE IF NOT EXISTS computer_operator_verified_transitions (
                workspace_id TEXT NOT NULL,
                from_state_fingerprint TEXT NOT NULL,
                strategy_key TEXT NOT NULL,
                expected_effect_fingerprint TEXT NOT NULL,
                success_count INTEGER NOT NULL,
                confidence_total REAL NOT NULL,
                duration_total_ms INTEGER NOT NULL,
                first_verified_at TEXT NOT NULL,
                last_verified_at TEXT NOT NULL,
                PRIMARY KEY (
                    workspace_id,
                    from_state_fingerprint,
                    strategy_key,
                    expected_effect_fingerprint
                )
            );

            CREATE INDEX IF NOT EXISTS ix_computer_operator_transition_lookup
            ON computer_operator_verified_transitions(
                workspace_id,
                from_state_fingerprint,
                success_count DESC,
                last_verified_at DESC
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
            """;

        command.ExecuteNonQuery();

        return connection;
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
                "State fingerprint phải là SHA-256 hex 64 ký tự.",
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

        return normalized.Length <= maximum
            ? normalized
            : normalized[..maximum];
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
}
