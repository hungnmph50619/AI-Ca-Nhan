using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IModelLabDatasetStore
{
    ModelLabDatasetStatus GetStatus();
    IReadOnlyList<ModelLabDatasetSummary> GetAll();
    ModelLabDatasetSummary? Get(string datasetId);
    IReadOnlyList<ModelLabDatasetVersion> GetVersions(string datasetId);
    ModelLabDatasetVersion? GetVersion(string datasetId, int version);
    ModelLabDatasetSummary Create(CreateModelLabDatasetRequest request);
    ModelLabDatasetVersion CreateVersion(
        string datasetId,
        CreateModelLabDatasetVersionRequest request);
}

public sealed class SqliteModelLabDatasetStore : IModelLabDatasetStore
{
    public const int MaximumDatasets = 100;
    public const int MaximumVersionsPerDataset = 100;
    public const int MaximumItemsPerVersion = 2_000;
    public const int MaximumPayloadBytes = 2 * 1024 * 1024;
    public const string StorageKind = "local-sqlite";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly string _databasePath;
    private readonly IWorkspaceContextAccessor _workspace;
    private bool _initialized;

    public SqliteModelLabDatasetStore(
        IConfiguration configuration,
        IWorkspaceContextAccessor workspace)
    {
        _workspace = workspace;

        var configuredRoot = configuration["ModelLab:Root"]?.Trim();
        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".personalai");
        }

        var directory = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(localData, "PersonalAI", "ModelLab")
            : Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(configuredRoot));

        Directory.CreateDirectory(directory);
        _databasePath = Path.Combine(directory, "datasets.db");
    }

    public ModelLabDatasetStatus GetStatus()
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            var workspaceId = _workspace.CurrentWorkspaceId;
            var datasets = Count(
                connection,
                "SELECT COUNT(*) FROM model_lab_datasets WHERE workspace_id = $workspaceId;",
                workspaceId);
            var versions = Count(
                connection,
                "SELECT COUNT(*) FROM model_lab_dataset_versions WHERE workspace_id = $workspaceId;",
                workspaceId);

            return new ModelLabDatasetStatus(
                PersonalAiRelease.Version,
                StorageKind,
                workspaceId,
                datasets,
                versions,
                MaximumDatasets,
                MaximumVersionsPerDataset,
                MaximumItemsPerVersion,
                MaximumPayloadBytes,
                VersionsImmutable: true);
        }
    }

    public IReadOnlyList<ModelLabDatasetSummary> GetAll()
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT d.dataset_id, d.name, d.description,
                       d.created_at, d.updated_at,
                       v.version, v.item_count, v.content_sha256
                FROM model_lab_datasets d
                JOIN model_lab_dataset_versions v
                  ON v.workspace_id = d.workspace_id
                 AND v.dataset_id = d.dataset_id
                 AND v.version = (
                    SELECT MAX(v2.version)
                    FROM model_lab_dataset_versions v2
                    WHERE v2.workspace_id = d.workspace_id
                      AND v2.dataset_id = d.dataset_id
                 )
                WHERE d.workspace_id = $workspaceId
                ORDER BY d.updated_at DESC, d.dataset_id ASC;
                """;
            command.Parameters.AddWithValue(
                "$workspaceId",
                _workspace.CurrentWorkspaceId);

            using var reader = command.ExecuteReader();
            var results = new List<ModelLabDatasetSummary>();
            while (reader.Read())
                results.Add(ReadSummary(reader));
            return results;
        }
    }

    public ModelLabDatasetSummary? Get(string datasetId)
    {
        var id = ValidateId(datasetId);
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            return GetSummaryInternal(connection, id);
        }
    }

    public IReadOnlyList<ModelLabDatasetVersion> GetVersions(string datasetId)
    {
        var id = ValidateId(datasetId);
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            if (GetSummaryInternal(connection, id) is null)
                return [];

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT dataset_id, version, items_json, item_count,
                       content_sha256, note, created_at
                FROM model_lab_dataset_versions
                WHERE workspace_id = $workspaceId
                  AND dataset_id = $datasetId
                ORDER BY version DESC;
                """;
            command.Parameters.AddWithValue(
                "$workspaceId",
                _workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue("$datasetId", id);

            using var reader = command.ExecuteReader();
            var results = new List<ModelLabDatasetVersion>();
            while (reader.Read())
                results.Add(ReadVersion(reader));
            return results;
        }
    }

    public ModelLabDatasetVersion? GetVersion(string datasetId, int version)
    {
        var id = ValidateId(datasetId);
        if (version < 1)
            throw new ModelLabDatasetValidationException(
                "Version phải lớn hơn hoặc bằng 1.");

        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT dataset_id, version, items_json, item_count,
                       content_sha256, note, created_at
                FROM model_lab_dataset_versions
                WHERE workspace_id = $workspaceId
                  AND dataset_id = $datasetId
                  AND version = $version
                LIMIT 1;
                """;
            command.Parameters.AddWithValue(
                "$workspaceId",
                _workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue("$datasetId", id);
            command.Parameters.AddWithValue("$version", version);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadVersion(reader) : null;
        }
    }

    public ModelLabDatasetSummary Create(CreateModelLabDatasetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = ValidateId(request.Id);
        var name = ValidateName(request.Name);
        var description = ValidateDescription(request.Description);
        var note = ValidateNote(request.VersionNote);
        var snapshot = NormalizeItems(request.Items);

        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            if (Count(
                connection,
                "SELECT COUNT(*) FROM model_lab_datasets WHERE workspace_id = $workspaceId;",
                _workspace.CurrentWorkspaceId,
                transaction) >= MaximumDatasets)
            {
                throw new ModelLabDatasetValidationException(
                    $"Mỗi workspace hỗ trợ tối đa {MaximumDatasets} Model Lab datasets.");
            }

            if (GetSummaryInternal(connection, id, transaction) is not null)
                throw new ModelLabDatasetConflictException(
                    $"Dataset '{id}' đã tồn tại.");

            var now = DateTimeOffset.UtcNow;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO model_lab_datasets (
                        workspace_id, dataset_id, name, description,
                        created_at, updated_at)
                    VALUES (
                        $workspaceId, $datasetId, $name, $description,
                        $createdAt, $updatedAt);
                    """;
                command.Parameters.AddWithValue(
                    "$workspaceId",
                    _workspace.CurrentWorkspaceId);
                command.Parameters.AddWithValue("$datasetId", id);
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$description", description);
                command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
                command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
                command.ExecuteNonQuery();
            }

            InsertVersion(
                connection,
                transaction,
                id,
                version: 1,
                snapshot,
                note,
                now);

            transaction.Commit();
            return GetSummaryInternal(connection, id)
                ?? throw new InvalidOperationException(
                    "Không đọc lại được dataset vừa tạo.");
        }
    }

    public ModelLabDatasetVersion CreateVersion(
        string datasetId,
        CreateModelLabDatasetVersionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = ValidateId(datasetId);
        var note = ValidateNote(request.Note);
        var snapshot = NormalizeItems(request.Items);

        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            if (GetSummaryInternal(connection, id, transaction) is null)
                throw new KeyNotFoundException("Không tìm thấy Model Lab dataset.");

            var latestVersion = GetLatestVersion(connection, transaction, id);
            if (latestVersion >= MaximumVersionsPerDataset)
                throw new ModelLabDatasetValidationException(
                    $"Dataset hỗ trợ tối đa {MaximumVersionsPerDataset} versions.");

            if (request.ExpectedLatestVersion is not null &&
                request.ExpectedLatestVersion.Value != latestVersion)
            {
                throw new ModelLabDatasetConflictException(
                    $"Dataset đã thay đổi. Latest version hiện là {latestVersion}.");
            }

            var nextVersion = latestVersion + 1;
            var now = DateTimeOffset.UtcNow;
            InsertVersion(
                connection,
                transaction,
                id,
                nextVersion,
                snapshot,
                note,
                now);

            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText =
                    """
                    UPDATE model_lab_datasets
                    SET updated_at = $updatedAt
                    WHERE workspace_id = $workspaceId
                      AND dataset_id = $datasetId;
                    """;
                update.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
                update.Parameters.AddWithValue(
                    "$workspaceId",
                    _workspace.CurrentWorkspaceId);
                update.Parameters.AddWithValue("$datasetId", id);
                update.ExecuteNonQuery();
            }

            transaction.Commit();
            return GetVersion(id, nextVersion)
                ?? throw new InvalidOperationException(
                    "Không đọc lại được dataset version vừa tạo.");
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS model_lab_datasets (
                workspace_id TEXT NOT NULL,
                dataset_id TEXT NOT NULL,
                name TEXT NOT NULL,
                description TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (workspace_id, dataset_id)
            );

            CREATE TABLE IF NOT EXISTS model_lab_dataset_versions (
                workspace_id TEXT NOT NULL,
                dataset_id TEXT NOT NULL,
                version INTEGER NOT NULL,
                items_json TEXT NOT NULL,
                item_count INTEGER NOT NULL,
                content_sha256 TEXT NOT NULL,
                note TEXT NULL,
                created_at TEXT NOT NULL,
                PRIMARY KEY (workspace_id, dataset_id, version),
                FOREIGN KEY (workspace_id, dataset_id)
                    REFERENCES model_lab_datasets(workspace_id, dataset_id)
                    ON DELETE RESTRICT
            );

            CREATE INDEX IF NOT EXISTS ix_model_lab_versions_latest
                ON model_lab_dataset_versions(
                    workspace_id, dataset_id, version DESC);
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
        command.CommandText =
            "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    private ModelLabDatasetSummary? GetSummaryInternal(
        SqliteConnection connection,
        string id,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT d.dataset_id, d.name, d.description,
                   d.created_at, d.updated_at,
                   v.version, v.item_count, v.content_sha256
            FROM model_lab_datasets d
            JOIN model_lab_dataset_versions v
              ON v.workspace_id = d.workspace_id
             AND v.dataset_id = d.dataset_id
             AND v.version = (
                SELECT MAX(v2.version)
                FROM model_lab_dataset_versions v2
                WHERE v2.workspace_id = d.workspace_id
                  AND v2.dataset_id = d.dataset_id
             )
            WHERE d.workspace_id = $workspaceId
              AND d.dataset_id = $datasetId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$workspaceId",
            _workspace.CurrentWorkspaceId);
        command.Parameters.AddWithValue("$datasetId", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSummary(reader) : null;
    }

    private ModelLabDatasetSummary ReadSummary(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            _workspace.CurrentWorkspaceId,
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetString(7),
            DateTimeOffset.Parse(reader.GetString(3)),
            DateTimeOffset.Parse(reader.GetString(4)));

    private ModelLabDatasetVersion ReadVersion(SqliteDataReader reader)
    {
        var json = reader.GetString(2);
        var items = JsonSerializer.Deserialize<List<JsonElement>>(
            json,
            JsonOptions) ?? [];
        return new ModelLabDatasetVersion(
            reader.GetString(0),
            reader.GetInt32(1),
            items.Select(item => item.Clone()).ToArray(),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            _workspace.CurrentWorkspaceId,
            DateTimeOffset.Parse(reader.GetString(6)));
    }

    private void InsertVersion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string datasetId,
        int version,
        DatasetSnapshot snapshot,
        string? note,
        DateTimeOffset createdAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO model_lab_dataset_versions (
                workspace_id, dataset_id, version, items_json,
                item_count, content_sha256, note, created_at)
            VALUES (
                $workspaceId, $datasetId, $version, $items,
                $itemCount, $sha256, $note, $createdAt);
            """;
        command.Parameters.AddWithValue(
            "$workspaceId",
            _workspace.CurrentWorkspaceId);
        command.Parameters.AddWithValue("$datasetId", datasetId);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$items", snapshot.Json);
        command.Parameters.AddWithValue("$itemCount", snapshot.ItemCount);
        command.Parameters.AddWithValue("$sha256", snapshot.Sha256);
        command.Parameters.AddWithValue(
            "$note",
            (object?)note ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$createdAt",
            createdAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private int GetLatestVersion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string datasetId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT MAX(version)
            FROM model_lab_dataset_versions
            WHERE workspace_id = $workspaceId
              AND dataset_id = $datasetId;
            """;
        command.Parameters.AddWithValue(
            "$workspaceId",
            _workspace.CurrentWorkspaceId);
        command.Parameters.AddWithValue("$datasetId", datasetId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int Count(
        SqliteConnection connection,
        string sql,
        string workspaceId,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static DatasetSnapshot NormalizeItems(
        IReadOnlyList<JsonElement>? items)
    {
        if (items is null)
            throw new ModelLabDatasetValidationException(
                "Dataset items không được null.");
        if (items.Count > MaximumItemsPerVersion)
            throw new ModelLabDatasetValidationException(
                $"Mỗi version hỗ trợ tối đa {MaximumItemsPerVersion} items.");

        var clones = new List<JsonElement>(items.Count);
        foreach (var item in items)
        {
            if (item.ValueKind is JsonValueKind.Undefined)
                throw new ModelLabDatasetValidationException(
                    "Dataset item không được Undefined.");
            clones.Add(item.Clone());
        }

        var json = JsonSerializer.Serialize(clones, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaximumPayloadBytes)
            throw new ModelLabDatasetValidationException(
                $"Payload mỗi version tối đa {MaximumPayloadBytes} bytes.");

        var sha = Convert.ToHexString(
            SHA256.HashData(bytes)).ToLowerInvariant();
        return new DatasetSnapshot(json, clones.Count, sha);
    }

    private static string ValidateId(string? value)
    {
        var id = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (id.Length is < 2 or > 100 ||
            id.Any(character =>
                !(char.IsLetterOrDigit(character) ||
                  character is '-' or '_' or '.')))
        {
            throw new ModelLabDatasetValidationException(
                "Dataset ID phải dài 2–100 ký tự và chỉ gồm chữ, số, '.', '_' hoặc '-'.");
        }
        return id;
    }

    private static string ValidateName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length is < 2 or > 160)
            throw new ModelLabDatasetValidationException(
                "Dataset name phải có từ 2 đến 160 ký tự.");
        return name;
    }

    private static string ValidateDescription(string? value)
    {
        var description = (value ?? string.Empty).Trim();
        if (description.Length > 1_000)
            throw new ModelLabDatasetValidationException(
                "Dataset description tối đa 1000 ký tự.");
        return description;
    }

    private static string? ValidateNote(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var note = value.Trim();
        if (note.Length > 500)
            throw new ModelLabDatasetValidationException(
                "Version note tối đa 500 ký tự.");
        return note;
    }

    private sealed record DatasetSnapshot(
        string Json,
        int ItemCount,
        string Sha256);
}
