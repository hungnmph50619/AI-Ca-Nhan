using Microsoft.Data.Sqlite;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorExperienceRetentionPolicy(
    TimeSpan CandidateLifetime,
    TimeSpan FailureHotLifetime,
    TimeSpan RecoveryHotLifetime,
    TimeSpan TrustedExperienceHotLifetime,
    TimeSpan StrategyHotLifetime)
{
    public static ComputerOperatorExperienceRetentionPolicy Default { get; } =
        new(
            CandidateLifetime: TimeSpan.FromDays(14),
            FailureHotLifetime: TimeSpan.FromDays(30),
            RecoveryHotLifetime: TimeSpan.FromDays(90),
            TrustedExperienceHotLifetime: TimeSpan.FromDays(180),
            StrategyHotLifetime: TimeSpan.FromDays(180));
}

public sealed record ComputerOperatorExperienceMaintenanceResult(
    int DeletedCandidates,
    int ArchivedFailures,
    int ArchivedRecoveries,
    int ArchivedExperiences,
    int ArchivedStrategies,
    int RemainingHotEvents,
    string ArchiveProvider,
    string Reason);

public interface IComputerOperatorExperienceLifecycleService
{
    ComputerOperatorExperienceMaintenanceResult RunMaintenance(
        DateTimeOffset? nowUtc = null);

    ComputerOperatorExperienceMaintenanceResult RunMaintenanceIfDue(
        DateTimeOffset? nowUtc = null);
}

/// <summary>
/// Quản lý vòng đời raw experience event.
/// Trusted knowledge cô đọng ở pattern/timing store; raw history cũ chuyển sang cold archive.
/// Candidate chưa trusted hết hạn sớm và không được archive như knowledge.
/// </summary>
public sealed class ComputerOperatorExperienceLifecycleService(
    IConfiguration configuration,
    IWorkspaceContextAccessor workspace)
    : IComputerOperatorExperienceLifecycleService
{
    public static readonly TimeSpan MaintenanceInterval =
        TimeSpan.FromHours(6);

    private readonly object gate = new();
    private readonly string hotDatabasePath =
        ResolveDatabasePath(
            configuration,
            "computer-operator-experience.db");
    private readonly string archiveDatabasePath =
        ResolveDatabasePath(
            configuration,
            "computer-operator-experience-archive.db");
    private readonly ComputerOperatorExperienceRetentionPolicy policy =
        ComputerOperatorExperienceRetentionPolicy.Default;
    private DateTimeOffset lastMaintenanceUtc =
        DateTimeOffset.MinValue;

    public ComputerOperatorExperienceMaintenanceResult RunMaintenanceIfDue(
        DateTimeOffset? nowUtc = null)
    {
        var now =
            nowUtc ??
            DateTimeOffset.UtcNow;

        lock (gate)
        {
            if (now - lastMaintenanceUtc <
                MaintenanceInterval)
            {
                return Snapshot(
                    "Maintenance chưa đến hạn; giữ nguyên dữ liệu hot.");
            }
        }

        return RunMaintenance(now);
    }

    public ComputerOperatorExperienceMaintenanceResult RunMaintenance(
        DateTimeOffset? nowUtc = null)
    {
        var now =
            nowUtc ??
            DateTimeOffset.UtcNow;

        lock (gate)
        {
            EnsureDirectories();

            using var hot =
                Open(
                    hotDatabasePath);
            using var archive =
                Open(
                    archiveDatabasePath);

            EnsureHotSchema(hot);
            EnsureArchiveSchema(archive);

            using var transaction =
                hot.BeginTransaction();
            using var archiveTransaction =
                archive.BeginTransaction();

            var deletedCandidates =
                DeleteExpiredCandidates(
                    hot,
                    transaction,
                    now - policy.CandidateLifetime);

            var archivedFailures =
                ArchiveAndDelete(
                    hot,
                    transaction,
                    archive,
                    archiveTransaction,
                    ComputerOperatorExperienceKinds.Failure,
                    now - policy.FailureHotLifetime,
                    requireConsolidatedPattern: false);

            var archivedRecoveries =
                ArchiveAndDelete(
                    hot,
                    transaction,
                    archive,
                    archiveTransaction,
                    ComputerOperatorExperienceKinds.Recovery,
                    now - policy.RecoveryHotLifetime,
                    requireConsolidatedPattern: false);

            var archivedExperiences =
                ArchiveAndDelete(
                    hot,
                    transaction,
                    archive,
                    archiveTransaction,
                    ComputerOperatorExperienceKinds.Experience,
                    now - policy.TrustedExperienceHotLifetime,
                    requireConsolidatedPattern: true);

            var archivedStrategies =
                ArchiveAndDelete(
                    hot,
                    transaction,
                    archive,
                    archiveTransaction,
                    ComputerOperatorExperienceKinds.Strategy,
                    now - policy.StrategyHotLifetime,
                    requireConsolidatedPattern: false);

            archiveTransaction.Commit();
            transaction.Commit();

            CheckpointWal(hot);
            CheckpointWal(archive);

            lastMaintenanceUtc =
                now;

            var remaining =
                CountHotEvents(hot);

            return new(
                deletedCandidates,
                archivedFailures,
                archivedRecoveries,
                archivedExperiences,
                archivedStrategies,
                remaining,
                "sqlite-cold-archive",
                $"Đã dọn lifecycle: xóa {deletedCandidates} candidate hết hạn; archive {archivedFailures + archivedRecoveries + archivedExperiences + archivedStrategies} raw event cũ.");
        }
    }

    private ComputerOperatorExperienceMaintenanceResult Snapshot(
        string reason)
    {
        EnsureDirectories();

        using var hot =
            Open(
                hotDatabasePath);

        EnsureHotSchema(hot);

        return new(
            0,
            0,
            0,
            0,
            0,
            CountHotEvents(hot),
            "sqlite-cold-archive",
            reason);
    }

    private int DeleteExpiredCandidates(
        SqliteConnection hot,
        SqliteTransaction transaction,
        DateTimeOffset cutoff)
    {
        using var command =
            hot.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            """
            DELETE FROM computer_operator_experience_events
            WHERE workspace_id = $workspaceId
              AND kind = $kind
              AND verified = 0
              AND created_at < $cutoff;
            """;

        command.Parameters.AddWithValue(
            "$workspaceId",
            workspace.CurrentWorkspaceId);
        command.Parameters.AddWithValue(
            "$kind",
            ComputerOperatorExperienceKinds.Candidate);
        command.Parameters.AddWithValue(
            "$cutoff",
            cutoff.ToString("O"));

        return command.ExecuteNonQuery();
    }

    private int ArchiveAndDelete(
        SqliteConnection hot,
        SqliteTransaction hotTransaction,
        SqliteConnection archive,
        SqliteTransaction archiveTransaction,
        string kind,
        DateTimeOffset cutoff,
        bool requireConsolidatedPattern)
    {
        var rows =
            ReadArchivable(
                hot,
                hotTransaction,
                kind,
                cutoff,
                requireConsolidatedPattern);

        foreach (var row in rows)
        {
            InsertArchive(
                archive,
                archiveTransaction,
                row);
        }

        if (rows.Count == 0)
            return 0;

        using var delete =
            hot.CreateCommand();

        delete.Transaction =
            hotTransaction;

        var ids =
            rows.Select(
                    (_, index) =>
                        $"$id{index}")
                .ToArray();

        delete.CommandText =
            $"""
            DELETE FROM computer_operator_experience_events
            WHERE workspace_id = $workspaceId
              AND event_id IN ({string.Join(",", ids)});
            """;

        delete.Parameters.AddWithValue(
            "$workspaceId",
            workspace.CurrentWorkspaceId);

        for (var index = 0;
             index < rows.Count;
             index++)
        {
            delete.Parameters.AddWithValue(
                ids[index],
                rows[index].Id.ToString("D"));
        }

        delete.ExecuteNonQuery();

        return rows.Count;
    }

    private IReadOnlyList<ComputerOperatorExperienceEvent> ReadArchivable(
        SqliteConnection hot,
        SqliteTransaction transaction,
        string kind,
        DateTimeOffset cutoff,
        bool requireConsolidatedPattern)
    {
        using var command =
            hot.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            requireConsolidatedPattern
                ? """
                  SELECT
                      e.event_id,
                      e.workspace_id,
                      e.kind,
                      e.context_fingerprint,
                      e.failure_code,
                      e.strategy_key,
                      e.outcome,
                      e.duration_ms,
                      e.confidence,
                      e.verified,
                      e.created_at
                  FROM computer_operator_experience_events e
                  WHERE e.workspace_id = $workspaceId
                    AND e.kind = $kind
                    AND e.created_at < $cutoff
                    AND EXISTS (
                        SELECT 1
                        FROM computer_operator_experience_patterns p
                        WHERE p.workspace_id = e.workspace_id
                          AND p.context_fingerprint = e.context_fingerprint
                          AND p.failure_code = e.failure_code
                          AND p.strategy_key = e.strategy_key
                    )
                  ORDER BY e.created_at;
                  """
                : """
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
                    AND kind = $kind
                    AND created_at < $cutoff
                  ORDER BY created_at;
                  """;

        command.Parameters.AddWithValue(
            "$workspaceId",
            workspace.CurrentWorkspaceId);
        command.Parameters.AddWithValue(
            "$kind",
            kind);
        command.Parameters.AddWithValue(
            "$cutoff",
            cutoff.ToString("O"));

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

    private static void InsertArchive(
        SqliteConnection archive,
        SqliteTransaction transaction,
        ComputerOperatorExperienceEvent row)
    {
        using var command =
            archive.CreateCommand();

        command.Transaction =
            transaction;

        command.CommandText =
            """
            INSERT OR IGNORE INTO computer_operator_experience_archive (
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
                created_at,
                archived_at
            )
            VALUES (
                $id,
                $workspaceId,
                $kind,
                $context,
                $failure,
                $strategy,
                $outcome,
                $duration,
                $confidence,
                $verified,
                $createdAt,
                $archivedAt
            );
            """;

        command.Parameters.AddWithValue("$id", row.Id.ToString("D"));
        command.Parameters.AddWithValue("$workspaceId", row.WorkspaceId);
        command.Parameters.AddWithValue("$kind", row.Kind);
        command.Parameters.AddWithValue("$context", row.ContextFingerprint);
        command.Parameters.AddWithValue("$failure", row.FailureCode);
        command.Parameters.AddWithValue("$strategy", row.StrategyKey);
        command.Parameters.AddWithValue("$outcome", row.Outcome);
        command.Parameters.AddWithValue("$duration", row.DurationMilliseconds);
        command.Parameters.AddWithValue("$confidence", row.Confidence);
        command.Parameters.AddWithValue("$verified", row.Verified ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", row.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$archivedAt", DateTimeOffset.UtcNow.ToString("O"));

        command.ExecuteNonQuery();
    }

    private int CountHotEvents(
        SqliteConnection connection)
    {
        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT COUNT(*)
            FROM computer_operator_experience_events
            WHERE workspace_id = $workspaceId;
            """;

        command.Parameters.AddWithValue(
            "$workspaceId",
            workspace.CurrentWorkspaceId);

        return Convert.ToInt32(
            command.ExecuteScalar() ??
            0);
    }

    private static void CheckpointWal(
        SqliteConnection connection)
    {
        using var command =
            connection.CreateCommand();

        command.CommandText =
            "PRAGMA wal_checkpoint(PASSIVE);";

        _ =
            command.ExecuteScalar();
    }

    private static void EnsureHotSchema(
        SqliteConnection connection)
    {
        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
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
            """;

        command.ExecuteNonQuery();
    }

    private static void EnsureArchiveSchema(
        SqliteConnection connection)
    {
        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS computer_operator_experience_archive (
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
                created_at TEXT NOT NULL,
                archived_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_computer_operator_archive_workspace_created
            ON computer_operator_experience_archive(
                workspace_id,
                created_at DESC
            );
            """;

        command.ExecuteNonQuery();
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(
                hotDatabasePath)!);

        Directory.CreateDirectory(
            Path.GetDirectoryName(
                archiveDatabasePath)!);
    }

    private static SqliteConnection Open(
        string path)
    {
        var connection =
            new SqliteConnection(
                $"Data Source={path};Mode=ReadWriteCreate;Cache=Shared");

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

    private static string ResolveDatabasePath(
        IConfiguration configuration,
        string fileName)
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
            fileName);
    }
}
