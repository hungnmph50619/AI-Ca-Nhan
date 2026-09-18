using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PersonalAI.Web.Services;

public static class ToolActivityEventTypes
{
    public const string ProposalCreated = "proposal-created";
    public const string ExecutionDenied = "execution-denied";
    public const string ExecutionCompleted = "execution-completed";
    public const string AiSynthesisDenied = "ai-synthesis-denied";
    public const string AiSynthesisCompleted = "ai-synthesis-completed";
    public const string NativeContinuationDenied = "native-continuation-denied";
    public const string NativeContinuationCompleted = "native-continuation-completed";
}

public sealed record ToolActivityEvent(
    string EventType,
    string ToolName,
    Guid? ProposalId,
    Guid? InvocationId,
    string? PlanningMode,
    string? PlanningProvider,
    string? PlanningModel,
    IReadOnlyList<string> RequiredPermissions,
    bool? Confirmed,
    bool? ExternalConfirmed,
    string? Status,
    JsonElement? Arguments = null,
    JsonElement? Output = null,
    int? DurationMs = null);

public sealed record ToolActivityItem(
    Guid ActivityId,
    DateTimeOffset OccurredAt,
    string EventType,
    string ToolName,
    Guid? ProposalId,
    Guid? InvocationId,
    string? PlanningMode,
    string? PlanningProvider,
    string? PlanningModel,
    IReadOnlyList<string> RequiredPermissions,
    bool? Confirmed,
    bool? ExternalConfirmed,
    string? Status,
    string? ArgumentsSha256,
    string? OutputSha256,
    int? DurationMs);

public sealed record ToolActivityResponse(
    string Storage,
    int RetentionDays,
    int MaximumEntries,
    IReadOnlyList<ToolActivityItem> Items);

public interface IToolActivityStore
{
    void Record(ToolActivityEvent activity);
    ToolActivityResponse GetRecent(int limit = 30);
}

public sealed class SqliteToolActivityStore : IToolActivityStore
{
    public const int RetentionDays = 30;
    public const int MaximumEntries = 2_000;
    public const int MaximumQueryLimit = 100;

    private readonly object _gate = new();
    private readonly string _databasePath;
    private bool _initialized;

    public SqliteToolActivityStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalAI",
            "ToolAudit");
        Directory.CreateDirectory(directory);
        _databasePath = Path.Combine(directory, "tool-activity.db");
    }

    public void Record(ToolActivityEvent activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    INSERT INTO tool_activity (
                        activity_id,
                        occurred_at,
                        event_type,
                        tool_name,
                        proposal_id,
                        invocation_id,
                        planning_mode,
                        planning_provider,
                        planning_model,
                        required_permissions,
                        confirmed,
                        external_confirmed,
                        status,
                        arguments_sha256,
                        output_sha256,
                        duration_ms
                    )
                    VALUES (
                        $activityId,
                        $occurredAt,
                        $eventType,
                        $toolName,
                        $proposalId,
                        $invocationId,
                        $planningMode,
                        $planningProvider,
                        $planningModel,
                        $requiredPermissions,
                        $confirmed,
                        $externalConfirmed,
                        $status,
                        $argumentsSha256,
                        $outputSha256,
                        $durationMs
                    );
                    """;

                command.Parameters.AddWithValue("$activityId", Guid.NewGuid().ToString("D"));
                command.Parameters.AddWithValue("$occurredAt", DateTimeOffset.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$eventType", Normalize(activity.EventType, 80));
                command.Parameters.AddWithValue("$toolName", Normalize(activity.ToolName, 160));
                command.Parameters.AddWithValue("$proposalId", DbValue(activity.ProposalId?.ToString("D")));
                command.Parameters.AddWithValue("$invocationId", DbValue(activity.InvocationId?.ToString("D")));
                command.Parameters.AddWithValue("$planningMode", DbValue(NormalizeNullable(activity.PlanningMode, 80)));
                command.Parameters.AddWithValue("$planningProvider", DbValue(NormalizeNullable(activity.PlanningProvider, 120)));
                command.Parameters.AddWithValue("$planningModel", DbValue(NormalizeNullable(activity.PlanningModel, 160)));
                command.Parameters.AddWithValue(
                    "$requiredPermissions",
                    string.Join("|", activity.RequiredPermissions
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim().ToUpperInvariant())
                        .Distinct(StringComparer.OrdinalIgnoreCase)));
                command.Parameters.AddWithValue("$confirmed", DbBoolean(activity.Confirmed));
                command.Parameters.AddWithValue("$externalConfirmed", DbBoolean(activity.ExternalConfirmed));
                command.Parameters.AddWithValue("$status", DbValue(NormalizeNullable(activity.Status, 80)));
                command.Parameters.AddWithValue("$argumentsSha256", DbValue(ComputeSha256(activity.Arguments)));
                command.Parameters.AddWithValue("$outputSha256", DbValue(ComputeSha256(activity.Output)));
                command.Parameters.AddWithValue("$durationMs", activity.DurationMs is null ? DBNull.Value : activity.DurationMs.Value);
                command.ExecuteNonQuery();
            }

            Cleanup(connection);
        }
    }

    public ToolActivityResponse GetRecent(int limit = 30)
    {
        limit = Math.Clamp(limit, 1, MaximumQueryLimit);

        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            Cleanup(connection);

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    activity_id,
                    occurred_at,
                    event_type,
                    tool_name,
                    proposal_id,
                    invocation_id,
                    planning_mode,
                    planning_provider,
                    planning_model,
                    required_permissions,
                    confirmed,
                    external_confirmed,
                    status,
                    arguments_sha256,
                    output_sha256,
                    duration_ms
                FROM tool_activity
                ORDER BY occurred_at DESC, rowid DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);

            using var reader = command.ExecuteReader();
            var items = new List<ToolActivityItem>();
            while (reader.Read())
            {
                items.Add(new ToolActivityItem(
                    Guid.Parse(reader.GetString(0)),
                    DateTimeOffset.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    ReadGuid(reader, 4),
                    ReadGuid(reader, 5),
                    ReadNullableString(reader, 6),
                    ReadNullableString(reader, 7),
                    ReadNullableString(reader, 8),
                    ReadPermissions(reader.GetString(9)),
                    ReadNullableBoolean(reader, 10),
                    ReadNullableBoolean(reader, 11),
                    ReadNullableString(reader, 12),
                    ReadNullableString(reader, 13),
                    ReadNullableString(reader, 14),
                    reader.IsDBNull(15) ? null : reader.GetInt32(15)));
            }

            return new ToolActivityResponse(
                "local-sqlite",
                RetentionDays,
                MaximumEntries,
                items);
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS tool_activity (
                activity_id TEXT PRIMARY KEY,
                occurred_at TEXT NOT NULL,
                event_type TEXT NOT NULL,
                tool_name TEXT NOT NULL,
                proposal_id TEXT NULL,
                invocation_id TEXT NULL,
                planning_mode TEXT NULL,
                planning_provider TEXT NULL,
                planning_model TEXT NULL,
                required_permissions TEXT NOT NULL,
                confirmed INTEGER NULL,
                external_confirmed INTEGER NULL,
                status TEXT NULL,
                arguments_sha256 TEXT NULL,
                output_sha256 TEXT NULL,
                duration_ms INTEGER NULL
            );

            CREATE INDEX IF NOT EXISTS ix_tool_activity_occurred_at
                ON tool_activity(occurred_at DESC);
            """;
        command.ExecuteNonQuery();
        _initialized = true;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Shared");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void Cleanup(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                DELETE FROM tool_activity
                WHERE occurred_at < $cutoff;
                """;
            command.Parameters.AddWithValue(
                "$cutoff",
                DateTimeOffset.UtcNow.AddDays(-RetentionDays).ToString("O"));
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                DELETE FROM tool_activity
                WHERE rowid IN (
                    SELECT rowid
                    FROM tool_activity
                    ORDER BY occurred_at DESC, rowid DESC
                    LIMIT -1 OFFSET $maximumEntries
                );
                """;
            command.Parameters.AddWithValue("$maximumEntries", MaximumEntries);
            command.ExecuteNonQuery();
        }
    }

    private static object DbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static object DbBoolean(bool? value) =>
        value is null ? DBNull.Value : value.Value ? 1 : 0;

    private static string Normalize(string value, int maximumLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length > maximumLength)
        {
            normalized = normalized[..maximumLength];
        }

        return normalized;
    }

    private static string? NormalizeNullable(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Normalize(value, maximumLength);
    }

    private static string? ComputeSha256(JsonElement? value)
    {
        if (value is null)
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(value.Value.GetRawText());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static Guid? ReadGuid(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static bool? ReadNullableBoolean(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal) != 0;

    private static IReadOnlyList<string> ReadPermissions(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
