using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PersonalAI.Web.Evaluation.Regression;

namespace PersonalAI.Web.Services;

public interface IRegressionDatasetStore
{
    RegressionDatasetStatus GetStatus();
    IReadOnlyList<RegressionDatasetItem> GetAll();
    RegressionDatasetItem? Get(string caseId);
    RegressionDatasetItem Create(CreateRegressionDatasetItemRequest request);
    RegressionDatasetItem? Update(string caseId, UpdateRegressionDatasetItemRequest request);
    bool Delete(string caseId);
}

public sealed class SqliteRegressionDatasetStore : IRegressionDatasetStore
{
    public const int MaximumCases = 500;
    public const int MaximumPayloadBytes = 128 * 1024;
    public const string StorageKind = "local-sqlite";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private readonly string _databasePath;
    private readonly IWorkspaceContextAccessor _workspaceContext;
    private readonly ILogger<SqliteRegressionDatasetStore> _logger;
    private bool _initialized;

    public SqliteRegressionDatasetStore(
        IConfiguration configuration,
        IWorkspaceContextAccessor workspaceContext,
        ILogger<SqliteRegressionDatasetStore> logger)
    {
        _workspaceContext = workspaceContext;
        _logger = logger;

        var configuredRoot = configuration["Evaluation:Root"]?.Trim();
        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".personalai");
        }

        var directory = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(localData, "PersonalAI", "Evaluation")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredRoot));
        Directory.CreateDirectory(directory);
        _databasePath = Path.Combine(directory, "regression-dataset.db");
    }

    public RegressionDatasetStatus GetStatus()
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            var workspaceId = _workspaceContext.CurrentWorkspaceId;
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT COUNT(*), SUM(CASE WHEN enabled = 1 THEN 1 ELSE 0 END)
                FROM regression_cases
                WHERE workspace_id = $workspaceId;
                """;
            command.Parameters.AddWithValue("$workspaceId", workspaceId);
            using var reader = command.ExecuteReader();
            reader.Read();
            var count = reader.GetInt32(0);
            var enabled = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            return new RegressionDatasetStatus(
                PersonalAI.Web.Models.PersonalAiRelease.Version,
                StorageKind,
                workspaceId,
                count,
                enabled,
                MaximumCases,
                MaximumPayloadBytes);
        }
    }

    public IReadOnlyList<RegressionDatasetItem> GetAll()
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT case_id, payload_json
                FROM regression_cases
                WHERE workspace_id = $workspaceId
                ORDER BY updated_at DESC, case_id ASC;
                """;
            command.Parameters.AddWithValue(
                "$workspaceId",
                _workspaceContext.CurrentWorkspaceId);

            using var reader = command.ExecuteReader();
            var results = new List<RegressionDatasetItem>();
            while (reader.Read())
            {
                var item = Deserialize(reader.GetString(1), reader.GetString(0));
                if (item is not null)
                    results.Add(item);
            }
            return results;
        }
    }

    public RegressionDatasetItem? Get(string caseId)
    {
        var id = ValidateId(caseId);
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT payload_json
                FROM regression_cases
                WHERE workspace_id = $workspaceId
                  AND case_id = $caseId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue(
                "$workspaceId",
                _workspaceContext.CurrentWorkspaceId);
            command.Parameters.AddWithValue("$caseId", id);
            var payload = command.ExecuteScalar() as string;
            return payload is null ? null : Deserialize(payload, id);
        }
    }

    public RegressionDatasetItem Create(CreateRegressionDatasetItemRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = DateTimeOffset.UtcNow;
        var workspaceId = _workspaceContext.CurrentWorkspaceId;
        var item = Normalize(
            request.Id,
            request.Title,
            request.Category,
            request.Input,
            request.Expected,
            request.Metadata,
            request.Enabled,
            workspaceId,
            now,
            now);

        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            if (Count(connection, workspaceId) >= MaximumCases)
                throw new RegressionDatasetValidationException(
                    $"Mỗi workspace hỗ trợ tối đa {MaximumCases} regression cases.");

            using (var duplicate = connection.CreateCommand())
            {
                duplicate.CommandText =
                    """
                    SELECT 1
                    FROM regression_cases
                    WHERE workspace_id = $workspaceId
                      AND case_id = $caseId
                    LIMIT 1;
                    """;
                duplicate.Parameters.AddWithValue("$workspaceId", workspaceId);
                duplicate.Parameters.AddWithValue("$caseId", item.Id);
                if (duplicate.ExecuteScalar() is not null)
                    throw new RegressionDatasetValidationException(
                        $"Regression case '{item.Id}' đã tồn tại.");
            }

            InsertOrUpdate(connection, item, insertOnly: true);
            return item;
        }
    }

    public RegressionDatasetItem? Update(
        string caseId,
        UpdateRegressionDatasetItemRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = ValidateId(caseId);
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            var existing = GetInternal(connection, id);
            if (existing is null)
                return null;

            var updated = Normalize(
                existing.Id,
                request.Title,
                request.Category,
                request.Input,
                request.Expected,
                request.Metadata,
                request.Enabled,
                existing.WorkspaceId,
                existing.CreatedAt,
                DateTimeOffset.UtcNow);
            InsertOrUpdate(connection, updated, insertOnly: false);
            return updated;
        }
    }

    public bool Delete(string caseId)
    {
        var id = ValidateId(caseId);
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                DELETE FROM regression_cases
                WHERE workspace_id = $workspaceId
                  AND case_id = $caseId;
                """;
            command.Parameters.AddWithValue(
                "$workspaceId",
                _workspaceContext.CurrentWorkspaceId);
            command.Parameters.AddWithValue("$caseId", id);
            return command.ExecuteNonQuery() > 0;
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS regression_cases (
                workspace_id TEXT NOT NULL,
                case_id TEXT NOT NULL,
                category TEXT NOT NULL,
                enabled INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                PRIMARY KEY (workspace_id, case_id)
            );

            CREATE INDEX IF NOT EXISTS ix_regression_cases_workspace_category
                ON regression_cases(workspace_id, category, enabled);

            CREATE INDEX IF NOT EXISTS ix_regression_cases_workspace_updated
                ON regression_cases(workspace_id, updated_at DESC);
            """;
        command.ExecuteNonQuery();
        _initialized = true;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(
            $"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Shared");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();
        return connection;
    }

    private RegressionDatasetItem? GetInternal(
        SqliteConnection connection,
        string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT payload_json
            FROM regression_cases
            WHERE workspace_id = $workspaceId
              AND case_id = $caseId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$workspaceId",
            _workspaceContext.CurrentWorkspaceId);
        command.Parameters.AddWithValue("$caseId", id);
        var payload = command.ExecuteScalar() as string;
        return payload is null ? null : Deserialize(payload, id);
    }

    private static int Count(SqliteConnection connection, string workspaceId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM regression_cases WHERE workspace_id = $workspaceId;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void InsertOrUpdate(
        SqliteConnection connection,
        RegressionDatasetItem item,
        bool insertOnly)
    {
        var payload = JsonSerializer.Serialize(item, JsonOptions);
        var byteCount = Encoding.UTF8.GetByteCount(payload);
        if (byteCount > MaximumPayloadBytes)
            throw new RegressionDatasetValidationException(
                $"Regression case vượt quá giới hạn {MaximumPayloadBytes} bytes.");

        using var command = connection.CreateCommand();
        command.CommandText = insertOnly
            ? """
              INSERT INTO regression_cases (
                  workspace_id, case_id, category, enabled,
                  created_at, updated_at, payload_json)
              VALUES (
                  $workspaceId, $caseId, $category, $enabled,
                  $createdAt, $updatedAt, $payload);
              """
            : """
              UPDATE regression_cases
              SET category = $category,
                  enabled = $enabled,
                  updated_at = $updatedAt,
                  payload_json = $payload
              WHERE workspace_id = $workspaceId
                AND case_id = $caseId;
              """;
        command.Parameters.AddWithValue("$workspaceId", item.WorkspaceId);
        command.Parameters.AddWithValue("$caseId", item.Id);
        command.Parameters.AddWithValue("$category", item.Category);
        command.Parameters.AddWithValue("$enabled", item.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", item.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$payload", payload);
        command.ExecuteNonQuery();
    }

    private RegressionDatasetItem? Deserialize(string payload, string expectedId)
    {
        try
        {
            var item = JsonSerializer.Deserialize<RegressionDatasetItem>(
                payload,
                JsonOptions);
            if (item is null ||
                !string.Equals(item.Id, expectedId, StringComparison.Ordinal))
                return null;
            return item;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "Không đọc được regression case {CaseId}.",
                expectedId);
            return null;
        }
    }

    private static RegressionDatasetItem Normalize(
        string id,
        string title,
        string category,
        JsonElement input,
        JsonElement? expected,
        IReadOnlyDictionary<string, string>? metadata,
        bool enabled,
        string workspaceId,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var normalizedId = ValidateId(id);
        var normalizedTitle = (title ?? string.Empty).Trim();
        if (normalizedTitle.Length is < 2 or > 160)
            throw new RegressionDatasetValidationException(
                "Tiêu đề regression case phải có từ 2 đến 160 ký tự.");

        var normalizedCategory = (category ?? string.Empty).Trim();
        if (normalizedCategory.Length is < 2 or > 120)
            throw new RegressionDatasetValidationException(
                "Category phải có từ 2 đến 120 ký tự.");

        if (input.ValueKind is JsonValueKind.Undefined)
            throw new RegressionDatasetValidationException(
                "Regression case phải có input.");
        if (expected is { ValueKind: JsonValueKind.Undefined })
            throw new RegressionDatasetValidationException(
                "Expected không hợp lệ.");

        var normalizedMetadata =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (metadata is not null)
        {
            if (metadata.Count > 20)
                throw new RegressionDatasetValidationException(
                    "Metadata hỗ trợ tối đa 20 mục.");

            foreach (var pair in metadata)
            {
                var key = (pair.Key ?? string.Empty).Trim();
                var value = (pair.Value ?? string.Empty).Trim();
                if (key.Length is < 1 or > 40 || value.Length > 240)
                    throw new RegressionDatasetValidationException(
                        "Metadata key phải 1–40 ký tự và value tối đa 240 ký tự.");
                normalizedMetadata[key] = value;
            }
        }

        var item = new RegressionDatasetItem(
            normalizedId,
            normalizedTitle,
            normalizedCategory,
            input.Clone(),
            expected?.Clone(),
            normalizedMetadata,
            enabled,
            workspaceId,
            createdAt,
            updatedAt);

        var payload = JsonSerializer.Serialize(item, JsonOptions);
        if (Encoding.UTF8.GetByteCount(payload) > MaximumPayloadBytes)
            throw new RegressionDatasetValidationException(
                $"Regression case vượt quá giới hạn {MaximumPayloadBytes} bytes.");
        return item;
    }

    private static string ValidateId(string? value)
    {
        var id = (value ?? string.Empty).Trim();
        if (id.Length is < 1 or > 120 ||
            id.Any(character =>
                !(char.IsLetterOrDigit(character) ||
                  character is '-' or '_' or '.')))
            throw new RegressionDatasetValidationException(
                "Case ID chỉ được gồm chữ, số, dấu chấm, gạch dưới, gạch ngang và tối đa 120 ký tự.");
        return id;
    }
}
