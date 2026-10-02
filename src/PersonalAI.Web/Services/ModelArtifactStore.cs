using System.Security.Cryptography;
using System.Text.Json;
using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IModelArtifactStore
{
    ModelArtifactStoreStatus GetStatus();
    IReadOnlyList<ModelArtifact> GetAll();
    ModelArtifact? Get(Guid id);
    ModelArtifact Register(RegisterModelArtifactRequest request);
    ModelArtifact UpdateStatus(Guid id, UpdateModelArtifactStatusRequest request);
}

public sealed class JsonModelArtifactStore(
    IConfiguration configuration,
    IWorkspaceContextAccessor workspace) : IModelArtifactStore
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ModelArtifactStoreStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.Status == ModelArtifactStatuses.Candidate),
            all.Count(x => x.Status == ModelArtifactStatuses.Validated),
            all.Count(x => x.Status == ModelArtifactStatuses.Rejected),
            all.Count(x => x.Status == ModelArtifactStatuses.Archived),
            Persistent: true,
            IntegrityVerifiedOnRegister: true);
    }

    public IReadOnlyList<ModelArtifact> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public ModelArtifact? Get(Guid id) =>
        GetAll().FirstOrDefault(x => x.Id == id);

    public ModelArtifact Register(RegisterModelArtifactRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = Normalize(request.Name, "Name", 160);
        var version = Normalize(request.Version, "Version", 80);
        var baseModel = Normalize(request.BaseModel, "BaseModel", 200);
        var datasetId = Normalize(request.DatasetId, "DatasetId", 120);
        var method = Normalize(request.TrainingMethod, "TrainingMethod", 80).ToLowerInvariant();
        var path = Path.GetFullPath(request.ArtifactPath ?? string.Empty);

        if (!File.Exists(path))
            throw new ModelArtifactValidationException("Artifact file không tồn tại.");

        var info = new FileInfo(path);
        if (info.Length <= 0)
            throw new ModelArtifactValidationException("Artifact file rỗng.");

        var sha = ComputeSha(path);
        var now = DateTimeOffset.UtcNow;
        var artifact = new ModelArtifact(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            name,
            version,
            baseModel,
            request.TrainingJobId,
            datasetId,
            request.DatasetVersion,
            request.DatasetSha256,
            method,
            path,
            sha,
            info.Length,
            request.Metrics.Clone(),
            ModelArtifactStatuses.Candidate,
            now,
            now);

        lock (_gate)
        {
            var list = Load();
            list.Add(artifact);
            Save(list);
        }

        return artifact;
    }

    public ModelArtifact UpdateStatus(Guid id, UpdateModelArtifactStatusRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var status = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (!ModelArtifactStatuses.IsValid(status))
            throw new ModelArtifactValidationException("Trạng thái artifact không hợp lệ.");

        lock (_gate)
        {
            var list = Load();
            var index = list.FindIndex(x => x.Id == id);
            if (index < 0)
                throw new KeyNotFoundException("Không tìm thấy model artifact.");

            list[index] = list[index] with
            {
                Status = status,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Save(list);
            return list[index];
        }
    }

    private List<ModelArtifact> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<ModelArtifact>>(
                File.ReadAllText(path), Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<ModelArtifact> items)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(items, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_root, $"model-artifacts-{safe}.json");
    }

    private static string ComputeSha(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Normalize(string? value, string field, int max)
    {
        var result = (value ?? string.Empty).Trim();
        if (result.Length is < 1 || result.Length > max)
            throw new ModelArtifactValidationException(
                $"{field} phải có từ 1 đến {max} ký tự.");
        return result;
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["ModelLab:Root"];
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "ModelLab");
        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "Registry");
    }
}
