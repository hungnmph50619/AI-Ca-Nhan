using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ITrainingJobStore
{
    TrainingJobStatus GetStatus();
    IReadOnlyList<TrainingJob> GetAll();
    TrainingJob? Get(Guid id);
    TrainingJob Create(CreateTrainingJobRequest request);
    TrainingJob? Cancel(Guid id);
}

public sealed class SqliteTrainingJobStore(
    IConfiguration configuration,
    IWorkspaceContextAccessor workspace,
    IModelLabDatasetStore datasets) : ITrainingJobStore
{
    public const int MaximumJobsPerWorkspace = 500;
    public const string StorageKind = "local-sqlite";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly string _databasePath = ResolveDatabasePath(configuration);
    private bool _initialized;

    public TrainingJobStatus GetStatus()
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();

            var total = Count(connection,
                "SELECT COUNT(*) FROM model_lab_training_jobs WHERE workspace_id = $workspaceId;");
            var active = Count(connection,
                """
                SELECT COUNT(*)
                FROM model_lab_training_jobs
                WHERE workspace_id = $workspaceId
                  AND status NOT IN ('completed', 'failed', 'cancelled');
                """);

            return new TrainingJobStatus(
                PersonalAiRelease.Version,
                StorageKind,
                workspace.CurrentWorkspaceId,
                total,
                active,
                MaximumJobsPerWorkspace,
                Persistent: true,
                ConfigImmutable: true,
                TrainingExecutionEnabled: false);
        }
    }

    public IReadOnlyList<TrainingJob> GetAll()
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, workspace_id, dataset_id, dataset_version, dataset_sha256,
                       base_model, training_method, hyperparameters_json, seed,
                       status, created_at, started_at, completed_at, artifact_path,
                       metrics_json, failure_reason, config_sha256
                FROM model_lab_training_jobs
                WHERE workspace_id = $workspaceId
                ORDER BY created_at DESC;
                """;
            command.Parameters.AddWithValue("$workspaceId", workspace.CurrentWorkspaceId);

            using var reader = command.ExecuteReader();
            var result = new List<TrainingJob>();
            while (reader.Read())
                result.Add(Read(reader));
            return result;
        }
    }

    public TrainingJob? Get(Guid id)
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, workspace_id, dataset_id, dataset_version, dataset_sha256,
                       base_model, training_method, hyperparameters_json, seed,
                       status, created_at, started_at, completed_at, artifact_path,
                       metrics_json, failure_reason, config_sha256
                FROM model_lab_training_jobs
                WHERE workspace_id = $workspaceId AND id = $id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$workspaceId", workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue("$id", id.ToString("D"));

            using var reader = command.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }
    }

    public TrainingJob Create(CreateTrainingJobRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var datasetId = (request.DatasetId ?? string.Empty).Trim();
        if (datasetId.Length is < 1 or > 120)
            throw new TrainingJobValidationException("DatasetId phải có từ 1 đến 120 ký tự.");
        if (request.DatasetVersion < 1)
            throw new TrainingJobValidationException("DatasetVersion phải lớn hơn hoặc bằng 1.");

        var baseModel = NormalizeRequired(request.BaseModel, "BaseModel", 200);
        var method = NormalizeRequired(request.TrainingMethod, "TrainingMethod", 80).ToLowerInvariant();
        var parameters = NormalizeHyperparameters(request.Hyperparameters);
        if (request.Seed is < 0)
            throw new TrainingJobValidationException("Seed không được âm.");

        var source = datasets.GetVersion(datasetId, request.DatasetVersion)
            ?? throw new KeyNotFoundException("Không tìm thấy dataset version dùng để training.");

        if (!string.Equals(
                source.WorkspaceId,
                workspace.CurrentWorkspaceId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new TrainingJobValidationException(
                "Dataset version không thuộc workspace hiện tại.");
        }

        var expectedSha = request.ExpectedDatasetSha256?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(expectedSha) &&
            !string.Equals(expectedSha, source.ContentSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new TrainingJobConflictException(
                "Checksum dataset không khớp. Hãy tải lại dataset version trước khi tạo training job.");
        }

        var canonicalConfig = JsonSerializer.Serialize(new
        {
            workspaceId = workspace.CurrentWorkspaceId,
            datasetId = source.DatasetId,
            datasetVersion = source.Version,
            datasetSha256 = source.ContentSha256,
            baseModel,
            trainingMethod = method,
            hyperparameters = parameters,
            request.Seed
        }, JsonOptions);
        var configSha = Sha256(canonicalConfig);

        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();

            if (Count(connection,
                "SELECT COUNT(*) FROM model_lab_training_jobs WHERE workspace_id = $workspaceId;")
                >= MaximumJobsPerWorkspace)
            {
                throw new TrainingJobValidationException(
                    $"Mỗi workspace hỗ trợ tối đa {MaximumJobsPerWorkspace} training jobs.");
            }

            var id = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO model_lab_training_jobs (
                    id, workspace_id, dataset_id, dataset_version, dataset_sha256,
                    base_model, training_method, hyperparameters_json, seed,
                    status, created_at, started_at, completed_at, artifact_path,
                    metrics_json, failure_reason, config_sha256)
                VALUES (
                    $id, $workspaceId, $datasetId, $datasetVersion, $datasetSha256,
                    $baseModel, $trainingMethod, $hyperparametersJson, $seed,
                    $status, $createdAt, NULL, NULL, NULL,
                    NULL, NULL, $configSha256);
                """;
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.Parameters.AddWithValue("$workspaceId", workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue("$datasetId", source.DatasetId);
            command.Parameters.AddWithValue("$datasetVersion", source.Version);
            command.Parameters.AddWithValue("$datasetSha256", source.ContentSha256);
            command.Parameters.AddWithValue("$baseModel", baseModel);
            command.Parameters.AddWithValue("$trainingMethod", method);
            command.Parameters.AddWithValue("$hyperparametersJson", parameters.GetRawText());
            command.Parameters.AddWithValue("$seed", request.Seed is null ? DBNull.Value : request.Seed.Value);
            command.Parameters.AddWithValue("$status", TrainingJobStatuses.Pending);
            command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
            command.Parameters.AddWithValue("$configSha256", configSha);
            command.ExecuteNonQuery();

            return Get(id) ?? throw new InvalidOperationException(
                "Không đọc lại được training job vừa tạo.");
        }
    }

    public TrainingJob? Cancel(Guid id)
    {
        lock (_gate)
        {
            EnsureInitialized();
            using var connection = OpenConnection();

            var existing = Get(id);
            if (existing is null) return null;
            if (TrainingJobStatuses.IsTerminal(existing.Status))
                return existing;
            if (existing.Status == TrainingJobStatuses.Running)
            {
                throw new TrainingJobConflictException(
                    "v2.5.3 chưa có training executor nên không hỗ trợ huỷ job đang Running.");
            }

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE model_lab_training_jobs
                SET status = $status,
                    completed_at = $completedAt
                WHERE workspace_id = $workspaceId AND id = $id;
                """;
            command.Parameters.AddWithValue("$status", TrainingJobStatuses.Cancelled);
            command.Parameters.AddWithValue("$completedAt", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$workspaceId", workspace.CurrentWorkspaceId);
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.ExecuteNonQuery();

            return Get(id);
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS model_lab_training_jobs (
                id TEXT NOT NULL,
                workspace_id TEXT NOT NULL,
                dataset_id TEXT NOT NULL,
                dataset_version INTEGER NOT NULL,
                dataset_sha256 TEXT NOT NULL,
                base_model TEXT NOT NULL,
                training_method TEXT NOT NULL,
                hyperparameters_json TEXT NOT NULL,
                seed INTEGER NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL,
                started_at TEXT NULL,
                completed_at TEXT NULL,
                artifact_path TEXT NULL,
                metrics_json TEXT NULL,
                failure_reason TEXT NULL,
                config_sha256 TEXT NOT NULL,
                PRIMARY KEY (workspace_id, id)
            );

            CREATE INDEX IF NOT EXISTS ix_model_lab_training_jobs_created
                ON model_lab_training_jobs(workspace_id, created_at DESC);

            CREATE INDEX IF NOT EXISTS ix_model_lab_training_jobs_status
                ON model_lab_training_jobs(workspace_id, status);
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

    private int Count(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$workspaceId", workspace.CurrentWorkspaceId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static TrainingJob Read(SqliteDataReader reader)
    {
        var hyperparameters = JsonDocument.Parse(reader.GetString(7)).RootElement.Clone();
        JsonElement? metrics = reader.IsDBNull(14)
            ? null
            : JsonDocument.Parse(reader.GetString(14)).RootElement.Clone();

        return new TrainingJob(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            hyperparameters,
            reader.IsDBNull(8) ? null : reader.GetInt32(8),
            reader.GetString(9),
            DateTimeOffset.Parse(reader.GetString(10)),
            reader.IsDBNull(11) ? null : DateTimeOffset.Parse(reader.GetString(11)),
            reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12)),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            metrics,
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.GetString(16));
    }

    private static JsonElement NormalizeHyperparameters(JsonElement? value)
    {
        if (value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return JsonSerializer.SerializeToElement(new Dictionary<string, object?>());

        if (value.Value.ValueKind != JsonValueKind.Object)
            throw new TrainingJobValidationException("Hyperparameters phải là JSON object.");

        var raw = value.Value.GetRawText();
        if (Encoding.UTF8.GetByteCount(raw) > 64 * 1024)
            throw new TrainingJobValidationException("Hyperparameters vượt quá 64 KB.");

        return value.Value.Clone();
    }

    private static string NormalizeRequired(string? value, string field, int maximum)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 || normalized.Length > maximum)
            throw new TrainingJobValidationException(
                $"{field} phải có từ 1 đến {maximum} ký tự.");
        return normalized;
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string ResolveDatabasePath(IConfiguration configuration)
    {
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
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredRoot));

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "training-jobs.db");
    }
}
