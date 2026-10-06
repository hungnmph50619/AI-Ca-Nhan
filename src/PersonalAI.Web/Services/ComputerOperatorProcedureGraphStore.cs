using Microsoft.Data.Sqlite;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorProcedureNode(
    string StateFingerprint,
    int ObservationCount,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt);

public sealed record ComputerOperatorProcedureEdge(
    string FromStateFingerprint,
    string ToStateFingerprint,
    string StrategyKey,
    string ActionKind,
    string ExpectedEffectFingerprint,
    int SuccessCount,
    double AverageConfidence,
    DateTimeOffset FirstVerifiedAt,
    DateTimeOffset LastVerifiedAt);

public interface IComputerOperatorProcedureGraphStore
{
    ComputerOperatorProcedureNode ObserveNode(
        string stateFingerprint);

    ComputerOperatorProcedureEdge RecordVerifiedEdge(
        string fromStateFingerprint,
        string toStateFingerprint,
        string strategyKey,
        string expectedEffectFingerprint,
        double confidence,
        string actionKind = "unknown");

    IReadOnlyList<ComputerOperatorProcedureEdge> GetOutgoing(
        string fromStateFingerprint,
        int limit = 20);

    IReadOnlyList<ComputerOperatorProcedureEdge> GetIncoming(
        string toStateFingerprint,
        int limit = 20);

    int CountNodes();

    int CountEdges();
}

/// <summary>
/// Graph thủ tục đã được xác minh.
/// Node là desktop state thực sự đã quan sát; edge chỉ được tạo sau action VERIFY PASS
/// và khi vòng quan sát kế tiếp xác nhận state đích thực tế.
/// Không lưu raw goal/text/screenshot.
/// </summary>
public sealed class SqliteComputerOperatorProcedureGraphStore(
    IConfiguration configuration,
    IWorkspaceContextAccessor workspace)
    : IComputerOperatorProcedureGraphStore
{
    public const int MaximumQueryLimit = 100;

    private readonly object gate = new();
    private readonly string databasePath =
        ResolveDatabasePath(configuration);
    private bool initialized;

    public ComputerOperatorProcedureNode ObserveNode(
        string stateFingerprint)
    {
        var state =
            NormalizeFingerprint(stateFingerprint);

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
                INSERT INTO computer_operator_procedure_nodes (
                    workspace_id,
                    state_fingerprint,
                    observation_count,
                    first_observed_at,
                    last_observed_at
                )
                VALUES (
                    $workspaceId,
                    $state,
                    1,
                    $now,
                    $now
                )
                ON CONFLICT(workspace_id, state_fingerprint)
                DO UPDATE SET
                    observation_count = observation_count + 1,
                    last_observed_at = excluded.last_observed_at;
                """;

            command.Parameters.AddWithValue(
                "$workspaceId",
                workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue(
                "$state",
                state);
            command.Parameters.AddWithValue(
                "$now",
                now.ToString("O"));

            command.ExecuteNonQuery();

            return ReadNode(
                connection,
                state)
                ?? throw new InvalidOperationException(
                    "Không đọc lại được procedure node vừa ghi.");
        }
    }

    public ComputerOperatorProcedureEdge RecordVerifiedEdge(
        string fromStateFingerprint,
        string toStateFingerprint,
        string strategyKey,
        string expectedEffectFingerprint,
        double confidence,
        string actionKind = "unknown")
    {
        var from =
            NormalizeFingerprint(fromStateFingerprint);
        var to =
            NormalizeFingerprint(toStateFingerprint);
        var effect =
            NormalizeFingerprint(expectedEffectFingerprint);
        var strategy =
            NormalizeDimension(strategyKey, 120);
        var action =
            NormalizeActionKind(actionKind);

        if (strategy.Length == 0)
        {
            throw new ArgumentException(
                "Strategy key không được trống.",
                nameof(strategyKey));
        }

        var now =
            DateTimeOffset.UtcNow;

        lock (gate)
        {
            EnsureInitialized();

            using var connection =
                OpenConnection();

            using var transaction =
                connection.BeginTransaction();

            UpsertNode(
                connection,
                transaction,
                from,
                now,
                incrementObservation: false);

            UpsertNode(
                connection,
                transaction,
                to,
                now,
                incrementObservation: false);

            using var command =
                connection.CreateCommand();

            command.Transaction =
                transaction;

            command.CommandText =
                """
                INSERT INTO computer_operator_procedure_edges (
                    workspace_id,
                    from_state_fingerprint,
                    to_state_fingerprint,
                    strategy_key,
                    action_kind,
                    expected_effect_fingerprint,
                    success_count,
                    confidence_total,
                    first_verified_at,
                    last_verified_at
                )
                VALUES (
                    $workspaceId,
                    $from,
                    $to,
                    $strategy,
                    $action,
                    $effect,
                    1,
                    $confidence,
                    $now,
                    $now
                )
                ON CONFLICT(
                    workspace_id,
                    from_state_fingerprint,
                    to_state_fingerprint,
                    strategy_key,
                    expected_effect_fingerprint
                )
                DO UPDATE SET
                    success_count = success_count + 1,
                    confidence_total = confidence_total + excluded.confidence_total,
                    action_kind = excluded.action_kind,
                    last_verified_at = excluded.last_verified_at;
                """;

            command.Parameters.AddWithValue(
                "$workspaceId",
                workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue("$from", from);
            command.Parameters.AddWithValue("$to", to);
            command.Parameters.AddWithValue("$strategy", strategy);
            command.Parameters.AddWithValue("$action", action);
            command.Parameters.AddWithValue("$effect", effect);
            command.Parameters.AddWithValue(
                "$confidence",
                Math.Clamp(confidence, 0, 1));
            command.Parameters.AddWithValue(
                "$now",
                now.ToString("O"));

            command.ExecuteNonQuery();
            transaction.Commit();

            return ReadEdge(
                connection,
                from,
                to,
                strategy,
                effect)
                ?? throw new InvalidOperationException(
                    "Không đọc lại được procedure edge vừa ghi.");
        }
    }

    public IReadOnlyList<ComputerOperatorProcedureEdge> GetOutgoing(
        string fromStateFingerprint,
        int limit = 20) =>
        ReadEdges(
            "from_state_fingerprint",
            NormalizeFingerprint(fromStateFingerprint),
            limit);

    public IReadOnlyList<ComputerOperatorProcedureEdge> GetIncoming(
        string toStateFingerprint,
        int limit = 20) =>
        ReadEdges(
            "to_state_fingerprint",
            NormalizeFingerprint(toStateFingerprint),
            limit);

    public int CountNodes() =>
        Count(
            "computer_operator_procedure_nodes");

    public int CountEdges() =>
        Count(
            "computer_operator_procedure_edges");

    private IReadOnlyList<ComputerOperatorProcedureEdge> ReadEdges(
        string column,
        string state,
        int limit)
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
                $"""
                SELECT
                    from_state_fingerprint,
                    to_state_fingerprint,
                    strategy_key,
                    action_kind,
                    expected_effect_fingerprint,
                    success_count,
                    confidence_total,
                    first_verified_at,
                    last_verified_at
                FROM computer_operator_procedure_edges
                WHERE workspace_id = $workspaceId
                  AND {column} = $state
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
                "$state",
                state);
            command.Parameters.AddWithValue(
                "$limit",
                resolvedLimit);

            var result =
                new List<ComputerOperatorProcedureEdge>();

            using var reader =
                command.ExecuteReader();

            while (reader.Read())
            {
                var edge =
                    ReadCurrentEdge(reader);

                if (edge is not null)
                    result.Add(edge);
            }

            return result;
        }
    }

    private int Count(
        string table)
    {
        lock (gate)
        {
            EnsureInitialized();

            using var connection =
                OpenConnection();

            using var command =
                connection.CreateCommand();

            command.CommandText =
                $"""
                SELECT COUNT(*)
                FROM {table}
                WHERE workspace_id = $workspaceId;
                """;

            command.Parameters.AddWithValue(
                "$workspaceId",
                workspace.CurrentWorkspaceId);

            return Convert.ToInt32(
                command.ExecuteScalar() ??
                0);
        }
    }

    private void UpsertNode(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string state,
        DateTimeOffset now,
        bool incrementObservation)
    {
        using var command =
            connection.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            incrementObservation
                ? """
                  INSERT INTO computer_operator_procedure_nodes (
                      workspace_id,
                      state_fingerprint,
                      observation_count,
                      first_observed_at,
                      last_observed_at
                  )
                  VALUES ($workspaceId, $state, 1, $now, $now)
                  ON CONFLICT(workspace_id, state_fingerprint)
                  DO UPDATE SET
                      observation_count = observation_count + 1,
                      last_observed_at = excluded.last_observed_at;
                  """
                : """
                  INSERT INTO computer_operator_procedure_nodes (
                      workspace_id,
                      state_fingerprint,
                      observation_count,
                      first_observed_at,
                      last_observed_at
                  )
                  VALUES ($workspaceId, $state, 0, $now, $now)
                  ON CONFLICT(workspace_id, state_fingerprint)
                  DO NOTHING;
                  """;

        command.Parameters.AddWithValue(
            "$workspaceId",
            workspace.CurrentWorkspaceId);
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue(
            "$now",
            now.ToString("O"));

        command.ExecuteNonQuery();
    }

    private ComputerOperatorProcedureNode? ReadNode(
        SqliteConnection connection,
        string state)
    {
        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                state_fingerprint,
                observation_count,
                first_observed_at,
                last_observed_at
            FROM computer_operator_procedure_nodes
            WHERE workspace_id = $workspaceId
              AND state_fingerprint = $state
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$workspaceId",
            workspace.CurrentWorkspaceId);
        command.Parameters.AddWithValue("$state", state);

        using var reader =
            command.ExecuteReader();

        if (!reader.Read() ||
            !DateTimeOffset.TryParse(
                reader.GetString(2),
                out var first) ||
            !DateTimeOffset.TryParse(
                reader.GetString(3),
                out var last))
        {
            return null;
        }

        return new(
            reader.GetString(0),
            reader.GetInt32(1),
            first,
            last);
    }

    private ComputerOperatorProcedureEdge? ReadEdge(
        SqliteConnection connection,
        string from,
        string to,
        string strategy,
        string effect)
    {
        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                from_state_fingerprint,
                to_state_fingerprint,
                strategy_key,
                action_kind,
                expected_effect_fingerprint,
                success_count,
                confidence_total,
                first_verified_at,
                last_verified_at
            FROM computer_operator_procedure_edges
            WHERE workspace_id = $workspaceId
              AND from_state_fingerprint = $from
              AND to_state_fingerprint = $to
              AND strategy_key = $strategy
              AND expected_effect_fingerprint = $effect
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$workspaceId",
            workspace.CurrentWorkspaceId);
        command.Parameters.AddWithValue("$from", from);
        command.Parameters.AddWithValue("$to", to);
        command.Parameters.AddWithValue("$strategy", strategy);
        command.Parameters.AddWithValue("$effect", effect);

        using var reader =
            command.ExecuteReader();

        return reader.Read()
            ? ReadCurrentEdge(reader)
            : null;
    }

    private static ComputerOperatorProcedureEdge? ReadCurrentEdge(
        SqliteDataReader reader)
    {
        if (!DateTimeOffset.TryParse(
                reader.GetString(7),
                out var first) ||
            !DateTimeOffset.TryParse(
                reader.GetString(8),
                out var last))
        {
            return null;
        }

        var count =
            Math.Max(
                1,
                reader.GetInt32(5));

        return new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            count,
            reader.GetDouble(6) / count,
            first,
            last);
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
            CREATE TABLE IF NOT EXISTS computer_operator_procedure_nodes (
                workspace_id TEXT NOT NULL,
                state_fingerprint TEXT NOT NULL,
                observation_count INTEGER NOT NULL,
                first_observed_at TEXT NOT NULL,
                last_observed_at TEXT NOT NULL,
                PRIMARY KEY (
                    workspace_id,
                    state_fingerprint
                )
            );

            CREATE TABLE IF NOT EXISTS computer_operator_procedure_edges (
                workspace_id TEXT NOT NULL,
                from_state_fingerprint TEXT NOT NULL,
                to_state_fingerprint TEXT NOT NULL,
                strategy_key TEXT NOT NULL,
                action_kind TEXT NOT NULL DEFAULT 'unknown',
                expected_effect_fingerprint TEXT NOT NULL,
                success_count INTEGER NOT NULL,
                confidence_total REAL NOT NULL,
                first_verified_at TEXT NOT NULL,
                last_verified_at TEXT NOT NULL,
                PRIMARY KEY (
                    workspace_id,
                    from_state_fingerprint,
                    to_state_fingerprint,
                    strategy_key,
                    expected_effect_fingerprint
                )
            );

            CREATE INDEX IF NOT EXISTS ix_computer_operator_procedure_outgoing
            ON computer_operator_procedure_edges(
                workspace_id,
                from_state_fingerprint,
                success_count DESC,
                last_verified_at DESC
            );

            CREATE INDEX IF NOT EXISTS ix_computer_operator_procedure_incoming
            ON computer_operator_procedure_edges(
                workspace_id,
                to_state_fingerprint,
                last_verified_at DESC
            );
            """;

        command.ExecuteNonQuery();

        EnsureActionKindColumn(
            connection);

        initialized = true;
    }

    private static void EnsureActionKindColumn(
        SqliteConnection connection)
    {
        using var inspect =
            connection.CreateCommand();

        inspect.CommandText =
            "PRAGMA table_info(computer_operator_procedure_edges);";

        var hasColumn =
            false;

        using (var reader =
            inspect.ExecuteReader())
        {
            while (reader.Read())
            {
                if (reader.GetString(1).Equals(
                        "action_kind",
                        StringComparison.OrdinalIgnoreCase))
                {
                    hasColumn =
                        true;
                    break;
                }
            }
        }

        if (hasColumn)
            return;

        using var alter =
            connection.CreateCommand();

        alter.CommandText =
            """
            ALTER TABLE computer_operator_procedure_edges
            ADD COLUMN action_kind TEXT NOT NULL DEFAULT 'unknown';
            """;

        alter.ExecuteNonQuery();
    }

    private static string NormalizeActionKind(
        string? value)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        if (normalized.Length == 0)
            return "unknown";

        var safe =
            new string(
                normalized
                    .Where(character =>
                        char.IsLetterOrDigit(character) ||
                        character is '-' or '_')
                    .Take(48)
                    .ToArray());

        return safe.Length == 0
            ? "unknown"
            : safe;
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
                "Procedure state fingerprint phải là SHA-256 hex 64 ký tự.",
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

public sealed class ComputerOperatorProcedureGraphSession(
    IComputerOperatorProcedureGraphStore graph)
{
    private sealed record PendingVerifiedAction(
        string FromStateFingerprint,
        string StrategyKey,
        string ActionKind,
        string ExpectedEffectFingerprint,
        double Confidence);

    private PendingVerifiedAction? pending;

    public ComputerOperatorProcedureEdge? ObserveState(
        string stateFingerprint)
    {
        graph.ObserveNode(
            stateFingerprint);

        if (pending is null)
            return null;

        var verified =
            pending;

        pending = null;

        return graph.RecordVerifiedEdge(
            verified.FromStateFingerprint,
            stateFingerprint,
            verified.StrategyKey,
            verified.ExpectedEffectFingerprint,
            verified.Confidence,
            verified.ActionKind);
    }

    public void RecordVerifiedAction(
        string fromStateFingerprint,
        string strategyKey,
        string expectedEffectFingerprint,
        double confidence,
        string actionKind = "unknown")
    {
        pending =
            new(
                fromStateFingerprint,
                strategyKey,
                NormalizeSessionActionKind(actionKind),
                expectedEffectFingerprint,
                confidence);
    }

    public void CancelPending()
    {
        pending = null;
    }

    private static string NormalizeSessionActionKind(
        string? value)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        return normalized.Length == 0
            ? "unknown"
            : normalized.Length <= 48
                ? normalized
                : normalized[..48];
    }
}
