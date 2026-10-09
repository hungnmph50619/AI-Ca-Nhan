using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace PersonalAI.Web.Services;

public static class ComputerOperatorContextCompatibilityKinds
{
    public const string ExactVersion = "exact-version";
    public const string SameFamily = "same-family";
    public const string Incompatible = "incompatible";
}

public sealed record ComputerOperatorProcedureContextDescriptor(
    string FamilyFingerprint,
    string VersionFingerprint);

public sealed record ComputerOperatorProcedureContextObservation(
    string StateFingerprint,
    string FamilyFingerprint,
    string VersionFingerprint,
    int ObservationCount,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt);

public sealed record ComputerOperatorContextCompatibilityAssessment(
    string Kind,
    bool Compatible,
    bool ExactVersion,
    double ConfidenceMultiplier,
    string Reason);

public interface IComputerOperatorProcedureContextService
{
    ComputerOperatorProcedureContextDescriptor Describe(
        ComputerOperatorDesktopState state);

    ComputerOperatorContextCompatibilityAssessment Assess(
        ComputerOperatorProcedureContextDescriptor current,
        ComputerOperatorProcedureContextObservation remembered);
}

public interface IComputerOperatorProcedureContextStore
{
    ComputerOperatorProcedureContextObservation Observe(
        string stateFingerprint,
        ComputerOperatorProcedureContextDescriptor context);

    ComputerOperatorProcedureContextObservation? Get(
        string stateFingerprint);

    IReadOnlyList<ComputerOperatorProcedureContextObservation> FindFamily(
        string familyFingerprint,
        int limit = 50);
}

/// <summary>
/// Tạo context family/version từ metadata desktop đã có.
/// Không lưu raw title, process, UI text hoặc screenshot; chỉ lưu SHA-256 fingerprint.
/// Family cố ý coarse để nối các phiên bản UI của cùng bề mặt; Version dùng schema UI chi tiết hơn.
/// </summary>
public sealed class ComputerOperatorProcedureContextService
    : IComputerOperatorProcedureContextService
{
    public ComputerOperatorProcedureContextDescriptor Describe(
        ComputerOperatorDesktopState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var process =
            Normalize(
                state.ForegroundWindow?.ProcessName);

        var rootRole =
            Normalize(
                state.StructuredScene?.Nodes
                    .OrderBy(node => node.Depth)
                    .FirstOrDefault()
                    ?.Role);

        var familyMaterial =
            string.Join(
                "|",
                "process=" + process,
                "scope=" + Normalize(state.CaptureScope),
                "root-role=" + rootRole,
                "structured=" + (state.StructuredScene is null ? "0" : "1"));

        var structure =
            state.StructuredScene is null
                ? "none"
                : string.Join(
                    ";",
                    state.StructuredScene.Nodes
                        .Where(node => !node.IsOffscreen)
                        .Take(80)
                        .Select(node =>
                            string.Join(
                                ":",
                                Normalize(node.Role),
                                Normalize(node.AutomationId),
                                node.Depth,
                                string.Join(
                                    ",",
                                    node.Patterns
                                        .OrderBy(
                                            pattern => pattern,
                                            StringComparer.OrdinalIgnoreCase)
                                        .Select(Normalize)))));

        var versionMaterial =
            string.Join(
                "|",
                familyMaterial,
                "frame=" +
                    Bucket(state.FrameWidth, 128) +
                    "x" +
                    Bucket(state.FrameHeight, 128),
                "structure=" + structure);

        return new(
            Hash(familyMaterial),
            Hash(versionMaterial));
    }

    public ComputerOperatorContextCompatibilityAssessment Assess(
        ComputerOperatorProcedureContextDescriptor current,
        ComputerOperatorProcedureContextObservation remembered)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(remembered);

        if (current.VersionFingerprint.Equals(
                remembered.VersionFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                ComputerOperatorContextCompatibilityKinds.ExactVersion,
                true,
                true,
                1.0,
                "Context version khớp chính xác.");
        }

        if (current.FamilyFingerprint.Equals(
                remembered.FamilyFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                ComputerOperatorContextCompatibilityKinds.SameFamily,
                true,
                false,
                0.72,
                "Cùng context family nhưng UI schema/version đã thay đổi; chỉ dùng làm kinh nghiệm tham khảo, không đủ điều kiện Fast Path.");
        }

        return new(
            ComputerOperatorContextCompatibilityKinds.Incompatible,
            false,
            false,
            0,
            "Context family khác; không tái sử dụng procedure fragment này.");
    }

    private static int Bucket(
        int value,
        int quantum) =>
        quantum <= 1
            ? value
            : Math.Max(
                0,
                (int)Math.Round(
                    value / (double)quantum,
                    MidpointRounding.AwayFromZero) * quantum);

    private static string Normalize(
        string? value)
    {
        var normalized =
            string.Join(
                " ",
                (value ?? string.Empty)
                    .Trim()
                    .ToLowerInvariant()
                    .Split(
                        [' ', '\t', '\r', '\n'],
                        StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= 80
            ? normalized
            : normalized[..80];
    }

    private static string Hash(
        string value) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(value)));
}

public sealed class SqliteComputerOperatorProcedureContextStore(
    IConfiguration configuration,
    IWorkspaceContextAccessor workspace)
    : IComputerOperatorProcedureContextStore
{
    private readonly object gate = new();
    private readonly string databasePath =
        ResolveDatabasePath(configuration);
    private bool initialized;

    public ComputerOperatorProcedureContextObservation Observe(
        string stateFingerprint,
        ComputerOperatorProcedureContextDescriptor context)
    {
        var state =
            NormalizeFingerprint(
                stateFingerprint);
        var family =
            NormalizeFingerprint(
                context.FamilyFingerprint);
        var version =
            NormalizeFingerprint(
                context.VersionFingerprint);
        var now =
            DateTimeOffset.UtcNow;

        lock (gate)
        {
            EnsureInitialized();

            using var connection =
                Open();

            using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO computer_operator_procedure_contexts (
                    workspace_id,
                    state_fingerprint,
                    family_fingerprint,
                    version_fingerprint,
                    observation_count,
                    first_observed_at,
                    last_observed_at
                )
                VALUES (
                    $workspaceId,
                    $state,
                    $family,
                    $version,
                    1,
                    $now,
                    $now
                )
                ON CONFLICT(
                    workspace_id,
                    state_fingerprint
                )
                DO UPDATE SET
                    family_fingerprint = excluded.family_fingerprint,
                    version_fingerprint = excluded.version_fingerprint,
                    observation_count = observation_count + 1,
                    last_observed_at = excluded.last_observed_at;
                """;

            command.Parameters.AddWithValue(
                "$workspaceId",
                workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue("$state", state);
            command.Parameters.AddWithValue("$family", family);
            command.Parameters.AddWithValue("$version", version);
            command.Parameters.AddWithValue("$now", now.ToString("O"));
            command.ExecuteNonQuery();

            return GetInternal(connection, state)
                ?? throw new InvalidOperationException(
                    "Không đọc lại được procedure context vừa ghi.");
        }
    }

    public ComputerOperatorProcedureContextObservation? Get(
        string stateFingerprint)
    {
        var state =
            NormalizeFingerprint(
                stateFingerprint);

        lock (gate)
        {
            EnsureInitialized();

            using var connection =
                Open();

            return GetInternal(
                connection,
                state);
        }
    }

    public IReadOnlyList<ComputerOperatorProcedureContextObservation> FindFamily(
        string familyFingerprint,
        int limit = 50)
    {
        var family =
            NormalizeFingerprint(
                familyFingerprint);
        var resolvedLimit =
            Math.Clamp(limit, 1, 200);

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
                    state_fingerprint,
                    family_fingerprint,
                    version_fingerprint,
                    observation_count,
                    first_observed_at,
                    last_observed_at
                FROM computer_operator_procedure_contexts
                WHERE workspace_id = $workspaceId
                  AND family_fingerprint = $family
                ORDER BY
                    last_observed_at DESC,
                    observation_count DESC
                LIMIT $limit;
                """;

            command.Parameters.AddWithValue(
                "$workspaceId",
                workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue("$family", family);
            command.Parameters.AddWithValue("$limit", resolvedLimit);

            var result =
                new List<ComputerOperatorProcedureContextObservation>();

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
    }

    private ComputerOperatorProcedureContextObservation? GetInternal(
        SqliteConnection connection,
        string state)
    {
        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                state_fingerprint,
                family_fingerprint,
                version_fingerprint,
                observation_count,
                first_observed_at,
                last_observed_at
            FROM computer_operator_procedure_contexts
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

        return reader.Read()
            ? ReadCurrent(reader)
            : null;
    }

    private static ComputerOperatorProcedureContextObservation? ReadCurrent(
        SqliteDataReader reader)
    {
        if (!DateTimeOffset.TryParse(
                reader.GetString(4),
                out var first) ||
            !DateTimeOffset.TryParse(
                reader.GetString(5),
                out var last))
        {
            return null;
        }

        return new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            first,
            last);
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
            CREATE TABLE IF NOT EXISTS computer_operator_procedure_contexts (
                workspace_id TEXT NOT NULL,
                state_fingerprint TEXT NOT NULL,
                family_fingerprint TEXT NOT NULL,
                version_fingerprint TEXT NOT NULL,
                observation_count INTEGER NOT NULL,
                first_observed_at TEXT NOT NULL,
                last_observed_at TEXT NOT NULL,
                PRIMARY KEY (
                    workspace_id,
                    state_fingerprint
                )
            );

            CREATE INDEX IF NOT EXISTS ix_computer_operator_context_family
            ON computer_operator_procedure_contexts(
                workspace_id,
                family_fingerprint,
                last_observed_at DESC
            );

            CREATE INDEX IF NOT EXISTS ix_computer_operator_context_version
            ON computer_operator_procedure_contexts(
                workspace_id,
                version_fingerprint,
                last_observed_at DESC
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
