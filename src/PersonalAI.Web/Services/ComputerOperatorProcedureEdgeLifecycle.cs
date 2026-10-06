using Microsoft.Data.Sqlite;

namespace PersonalAI.Web.Services;

public static class ComputerOperatorProcedureEdgeStatuses
{
    public const string Active = "active";
    public const string Superseded = "superseded";
}

public sealed record ComputerOperatorProcedureEdgeLifecycle(
    string FromStateFingerprint,
    string ToStateFingerprint,
    string StrategyKey,
    string ExpectedEffectFingerprint,
    string Status,
    string? SupersededByStrategyKey,
    DateTimeOffset? SupersededAt,
    double DecayMultiplier,
    double EffectiveConfidence,
    string Reason);

public sealed record ComputerOperatorProcedureSupersessionResult(
    int EvaluatedEdges,
    int SupersededEdges,
    string? WinningStrategyKey,
    string Reason);

public interface IComputerOperatorProcedureEdgeLifecycleService
{
    ComputerOperatorProcedureEdgeLifecycle Evaluate(
        ComputerOperatorProcedureEdge edge,
        DateTimeOffset? now = null);

    ComputerOperatorProcedureSupersessionResult Reconcile(
        string fromStateFingerprint);

    bool IsSuperseded(
        ComputerOperatorProcedureEdge edge);
}

/// <summary>
/// Quản lý tuổi thọ knowledge của Procedure Graph.
/// Edge lịch sử không bị xóa. Edge cũ bị decay theo tuổi; strategy chỉ bị supersede
/// khi một strategy khác cho cùng transition thực tế có bằng chứng mạnh hơn.
/// </summary>
public sealed class ComputerOperatorProcedureEdgeLifecycleService(
    IConfiguration configuration,
    IWorkspaceContextAccessor workspace,
    IComputerOperatorProcedureGraphStore graph)
    : IComputerOperatorProcedureEdgeLifecycleService
{
    public const int MinimumWinnerSuccesses = 3;
    public const double MinimumWinnerConfidence = 0.95;
    public const double MinimumConfidenceAdvantage = 0.02;

    private static readonly TimeSpan FreshWindow =
        TimeSpan.FromDays(30);
    private static readonly TimeSpan AgingWindow =
        TimeSpan.FromDays(90);
    private static readonly TimeSpan StaleWindow =
        TimeSpan.FromDays(180);

    private readonly object gate = new();
    private readonly string databasePath =
        ResolveDatabasePath(configuration);
    private bool initialized;

    public ComputerOperatorProcedureEdgeLifecycle Evaluate(
        ComputerOperatorProcedureEdge edge,
        DateTimeOffset? now = null)
    {
        var timestamp =
            now ?? DateTimeOffset.UtcNow;

        var metadata =
            GetMetadata(edge);

        if (metadata.Status.Equals(
                ComputerOperatorProcedureEdgeStatuses.Superseded,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                edge.FromStateFingerprint,
                edge.ToStateFingerprint,
                edge.StrategyKey,
                edge.ExpectedEffectFingerprint,
                ComputerOperatorProcedureEdgeStatuses.Superseded,
                metadata.SupersededByStrategyKey,
                metadata.SupersededAt,
                0,
                0,
                "Strategy đã bị supersede bởi strategy được xác minh tốt hơn; chỉ giữ làm lịch sử/audit.");
        }

        var age =
            timestamp - edge.LastVerifiedAt;

        var multiplier =
            age <= FreshWindow
                ? 1.0
                : age <= AgingWindow
                    ? 0.90
                    : age <= StaleWindow
                        ? 0.75
                        : 0.55;

        return new(
            edge.FromStateFingerprint,
            edge.ToStateFingerprint,
            edge.StrategyKey,
            edge.ExpectedEffectFingerprint,
            ComputerOperatorProcedureEdgeStatuses.Active,
            null,
            null,
            multiplier,
            edge.AverageConfidence * multiplier,
            multiplier >= 1
                ? "Knowledge còn mới; chưa decay."
                : $"Knowledge đã cũ {Math.Max(0, age.TotalDays):0} ngày; áp dụng decay multiplier={multiplier:0.00}, không xóa lịch sử.");
    }

    public ComputerOperatorProcedureSupersessionResult Reconcile(
        string fromStateFingerprint)
    {
        var outgoing =
            graph.GetOutgoing(
                fromStateFingerprint,
                limit: 100);

        var superseded =
            0;

        string? latestWinner =
            null;

        foreach (var group in outgoing.GroupBy(edge =>
                     string.Join(
                         "|",
                         edge.ToStateFingerprint,
                         edge.ActionKind,
                         edge.ExpectedEffectFingerprint),
                     StringComparer.OrdinalIgnoreCase))
        {
            var ranked =
                group
                    .Where(edge =>
                        !IsSuperseded(edge))
                    .OrderByDescending(edge =>
                        edge.AverageConfidence)
                    .ThenByDescending(edge =>
                        edge.SuccessCount)
                    .ThenByDescending(edge =>
                        edge.LastVerifiedAt)
                    .ToArray();

            if (ranked.Length < 2)
                continue;

            var winner =
                ranked[0];

            if (winner.SuccessCount <
                    MinimumWinnerSuccesses ||
                winner.AverageConfidence <
                    MinimumWinnerConfidence)
            {
                continue;
            }

            foreach (var loser in ranked.Skip(1))
            {
                var clearlyBetter =
                    winner.AverageConfidence >=
                        loser.AverageConfidence +
                        MinimumConfidenceAdvantage &&
                    winner.SuccessCount >=
                        loser.SuccessCount;

                var muchMoreEvidence =
                    winner.SuccessCount >=
                        loser.SuccessCount + 2 &&
                    winner.AverageConfidence >=
                        loser.AverageConfidence;

                if (!clearlyBetter &&
                    !muchMoreEvidence)
                {
                    continue;
                }

                MarkSuperseded(
                    loser,
                    winner.StrategyKey);

                superseded++;
                latestWinner =
                    winner.StrategyKey;
            }
        }

        return new(
            outgoing.Count,
            superseded,
            latestWinner,
            superseded == 0
                ? "Chưa có strategy cạnh tranh nào đủ bằng chứng để supersede knowledge hiện tại."
                : $"Đã supersede {superseded} strategy cũ; dữ liệu cũ vẫn được giữ để audit/fallback lịch sử.");
    }

    public bool IsSuperseded(
        ComputerOperatorProcedureEdge edge) =>
        GetMetadata(edge).Status.Equals(
            ComputerOperatorProcedureEdgeStatuses.Superseded,
            StringComparison.OrdinalIgnoreCase);

    private void MarkSuperseded(
        ComputerOperatorProcedureEdge loser,
        string winnerStrategyKey)
    {
        lock (gate)
        {
            EnsureInitialized();

            using var connection =
                Open();

            using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO computer_operator_procedure_edge_lifecycle (
                    workspace_id,
                    from_state_fingerprint,
                    to_state_fingerprint,
                    strategy_key,
                    expected_effect_fingerprint,
                    status,
                    superseded_by_strategy_key,
                    superseded_at
                )
                VALUES (
                    $workspaceId,
                    $from,
                    $to,
                    $strategy,
                    $effect,
                    $status,
                    $winner,
                    $at
                )
                ON CONFLICT(
                    workspace_id,
                    from_state_fingerprint,
                    to_state_fingerprint,
                    strategy_key,
                    expected_effect_fingerprint
                )
                DO UPDATE SET
                    status = excluded.status,
                    superseded_by_strategy_key = excluded.superseded_by_strategy_key,
                    superseded_at = excluded.superseded_at;
                """;

            command.Parameters.AddWithValue(
                "$workspaceId",
                workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue(
                "$from",
                loser.FromStateFingerprint);
            command.Parameters.AddWithValue(
                "$to",
                loser.ToStateFingerprint);
            command.Parameters.AddWithValue(
                "$strategy",
                loser.StrategyKey);
            command.Parameters.AddWithValue(
                "$effect",
                loser.ExpectedEffectFingerprint);
            command.Parameters.AddWithValue(
                "$status",
                ComputerOperatorProcedureEdgeStatuses.Superseded);
            command.Parameters.AddWithValue(
                "$winner",
                winnerStrategyKey);
            command.Parameters.AddWithValue(
                "$at",
                DateTimeOffset.UtcNow.ToString("O"));

            command.ExecuteNonQuery();
        }
    }

    private LifecycleMetadata GetMetadata(
        ComputerOperatorProcedureEdge edge)
    {
        lock (gate)
        {
            EnsureInitialized();

            using var connection =
                Open();

            using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                SELECT
                    status,
                    superseded_by_strategy_key,
                    superseded_at
                FROM computer_operator_procedure_edge_lifecycle
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
            command.Parameters.AddWithValue(
                "$from",
                edge.FromStateFingerprint);
            command.Parameters.AddWithValue(
                "$to",
                edge.ToStateFingerprint);
            command.Parameters.AddWithValue(
                "$strategy",
                edge.StrategyKey);
            command.Parameters.AddWithValue(
                "$effect",
                edge.ExpectedEffectFingerprint);

            using var reader =
                command.ExecuteReader();

            if (!reader.Read())
            {
                return new(
                    ComputerOperatorProcedureEdgeStatuses.Active,
                    null,
                    null);
            }

            DateTimeOffset? supersededAt =
                null;

            if (!reader.IsDBNull(2) &&
                DateTimeOffset.TryParse(
                    reader.GetString(2),
                    out var parsed))
            {
                supersededAt =
                    parsed;
            }

            return new(
                reader.GetString(0),
                reader.IsDBNull(1)
                    ? null
                    : reader.GetString(1),
                supersededAt);
        }
    }

    private void EnsureInitialized()
    {
        if (initialized)
            return;

        Directory.CreateDirectory(
            Path.GetDirectoryName(databasePath)!);

        using var connection =
            Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS computer_operator_procedure_edge_lifecycle (
                workspace_id TEXT NOT NULL,
                from_state_fingerprint TEXT NOT NULL,
                to_state_fingerprint TEXT NOT NULL,
                strategy_key TEXT NOT NULL,
                expected_effect_fingerprint TEXT NOT NULL,
                status TEXT NOT NULL,
                superseded_by_strategy_key TEXT NULL,
                superseded_at TEXT NULL,
                PRIMARY KEY (
                    workspace_id,
                    from_state_fingerprint,
                    to_state_fingerprint,
                    strategy_key,
                    expected_effect_fingerprint
                )
            );

            CREATE INDEX IF NOT EXISTS ix_computer_operator_edge_lifecycle_status
            ON computer_operator_procedure_edge_lifecycle(
                workspace_id,
                status,
                superseded_at
            );
            """;

        command.ExecuteNonQuery();
        initialized = true;
    }

    private SqliteConnection Open()
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

    private sealed record LifecycleMetadata(
        string Status,
        string? SupersededByStrategyKey,
        DateTimeOffset? SupersededAt);
}
