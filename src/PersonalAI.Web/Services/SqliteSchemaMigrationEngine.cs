using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace PersonalAI.Web.Services;

public sealed record SqliteMigrationStep(
    string Id,
    string Description,
    string ChecksumSeed,
    Action<SqliteConnection> Apply);

public sealed record SqliteMigrationState(
    string Component,
    int ExpectedMigrations,
    int AppliedMigrations,
    bool UpToDate,
    IReadOnlyList<string> AppliedIds);

public static class SqliteSchemaMigrationEngine
{
    private static readonly ConcurrentDictionary<string, SqliteMigrationState> States =
        new(StringComparer.OrdinalIgnoreCase);

    public static SqliteMigrationState Apply(
        SqliteConnection connection,
        string component,
        IReadOnlyList<SqliteMigrationStep> steps)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrWhiteSpace(component))
            throw new InvalidOperationException("Migration component không hợp lệ.");

        EnsureHistoryTable(connection);
        var applied = ReadHistory(connection);

        foreach (var step in steps)
        {
            var checksum = Checksum(step.ChecksumSeed);
            if (applied.TryGetValue(step.Id, out var recorded))
            {
                if (!recorded.Equals(checksum, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Migration checksum mismatch: {component}/{step.Id}.");
                }

                continue;
            }

            ExecuteNonQuery(connection, "BEGIN IMMEDIATE;");
            try
            {
                step.Apply(connection);

                using var insert = connection.CreateCommand();
                insert.CommandText =
                    """
                    INSERT INTO personalai_schema_migrations (
                        migration_id,
                        checksum,
                        description,
                        applied_at
                    )
                    VALUES ($id, $checksum, $description, $appliedAt);
                    """;
                insert.Parameters.AddWithValue("$id", step.Id);
                insert.Parameters.AddWithValue("$checksum", checksum);
                insert.Parameters.AddWithValue("$description", step.Description);
                insert.Parameters.AddWithValue(
                    "$appliedAt",
                    DateTimeOffset.UtcNow.ToString("O"));
                insert.ExecuteNonQuery();

                ExecuteNonQuery(connection, "COMMIT;");
                applied[step.Id] = checksum;
            }
            catch
            {
                try
                {
                    ExecuteNonQuery(connection, "ROLLBACK;");
                }
                catch
                {
                }
                throw;
            }
        }

        var ids = applied.Keys.Order(StringComparer.Ordinal).ToArray();
        var state = new SqliteMigrationState(
            component,
            steps.Count,
            steps.Count(step => applied.ContainsKey(step.Id)),
            steps.All(step => applied.ContainsKey(step.Id)),
            ids);
        States[component] = state;
        return state;
    }

    public static IReadOnlyList<SqliteMigrationState> Snapshot() =>
        States.Values
            .OrderBy(x => x.Component, StringComparer.Ordinal)
            .ToArray();

    public static void EnsureColumn(
        SqliteConnection connection,
        string table,
        string column,
        string definition)
    {
        using var inspect = connection.CreateCommand();
        inspect.CommandText = $"PRAGMA table_info({table});";
        using var reader = inspect.ExecuteReader();
        var exists = false;
        while (reader.Read())
        {
            if (reader.GetString(1).Equals(
                column,
                StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        reader.Close();
        if (exists)
            return;

        using var alter = connection.CreateCommand();
        alter.CommandText =
            $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }

    private static void ExecuteNonQuery(
        SqliteConnection connection,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void EnsureHistoryTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS personalai_schema_migrations (
                migration_id TEXT PRIMARY KEY,
                checksum TEXT NOT NULL,
                description TEXT NOT NULL,
                applied_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static Dictionary<string, string> ReadHistory(
        SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT migration_id, checksum
            FROM personalai_schema_migrations
            ORDER BY migration_id;
            """;
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    private static string Checksum(string value) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
