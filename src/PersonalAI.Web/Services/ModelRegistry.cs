using System.Text.Json;
using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IModelRegistry
{
    ModelRegistryStatus GetStatus();
    IReadOnlyList<ModelFamilySummary> GetFamilies();
    IReadOnlyList<ModelVersionRecord> GetAll();
    IReadOnlyList<ModelVersionRecord> GetFamily(string family);
    ModelVersionRecord? Get(Guid id);
    ModelVersionRecord Register(RegisterModelVersionRequest request);
    ModelVersionRecord UpdateStage(Guid id, UpdateModelDeploymentStageRequest request);
}

public sealed class JsonModelRegistry(
    IConfiguration configuration,
    IWorkspaceContextAccessor workspace,
    IModelArtifactStore artifacts) : IModelRegistry
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ModelRegistryStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Select(x => x.Family).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            all.Count,
            all.Count(x => x.Stage == ModelDeploymentStages.Candidate),
            all.Count(x => x.Stage == ModelDeploymentStages.Staging),
            all.Count(x => x.Stage == ModelDeploymentStages.Canary),
            all.Count(x => x.Stage == ModelDeploymentStages.Production),
            all.Count(x => x.Stage == ModelDeploymentStages.Deprecated),
            all.Count(x => x.Stage == ModelDeploymentStages.Rejected),
            all.Count(x => x.Stage == ModelDeploymentStages.Archived),
            Persistent: true,
            SingleProductionPerFamily: true);
    }

    public IReadOnlyList<ModelFamilySummary> GetFamilies() =>
        GetAll()
            .GroupBy(x => x.Family, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group.OrderByDescending(x => x.CreatedAt).ToArray();
                return new ModelFamilySummary(
                    ordered[0].Family,
                    ordered.Length,
                    ordered.FirstOrDefault(x => x.Stage == ModelDeploymentStages.Production)?.Version,
                    ordered.FirstOrDefault(x => x.Stage == ModelDeploymentStages.Canary)?.Version,
                    ordered[0].Version,
                    ordered.Max(x => x.UpdatedAt));
            })
            .OrderBy(x => x.Family, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<ModelVersionRecord> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public IReadOnlyList<ModelVersionRecord> GetFamily(string family)
    {
        var normalized = NormalizeFamily(family);
        return GetAll()
            .Where(x => x.Family.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CreatedAt)
            .ToArray();
    }

    public ModelVersionRecord? Get(Guid id) =>
        GetAll().FirstOrDefault(x => x.Id == id);

    public ModelVersionRecord Register(RegisterModelVersionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var family = NormalizeFamily(request.Family);
        var version = Normalize(request.Version, "Version", 80);
        var runtime = string.IsNullOrWhiteSpace(request.Runtime)
            ? "local"
            : Normalize(request.Runtime, "Runtime", 80).ToLowerInvariant();
        var notes = NormalizeOptional(request.Notes, 1000);
        var tasks = NormalizeTasks(request.CompatibleTasks);

        var artifact = artifacts.Get(request.ArtifactId)
            ?? throw new KeyNotFoundException("Không tìm thấy model artifact.");

        lock (_gate)
        {
            var list = Load();
            if (list.Any(x =>
                x.Family.Equals(family, StringComparison.OrdinalIgnoreCase) &&
                x.Version.Equals(version, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ModelRegistryConflictException(
                    $"Model family '{family}' đã có version '{version}'.");
            }

            var now = DateTimeOffset.UtcNow;
            var item = new ModelVersionRecord(
                Guid.NewGuid(),
                workspace.CurrentWorkspaceId,
                family,
                version,
                artifact.Id,
                artifact.ArtifactSha256,
                artifact.BaseModel,
                artifact.TrainingMethod,
                artifact.DatasetId,
                artifact.DatasetVersion,
                artifact.DatasetSha256,
                tasks,
                runtime,
                ModelDeploymentStages.Candidate,
                notes,
                now,
                now);

            list.Add(item);
            Save(list);
            return item;
        }
    }

    public ModelVersionRecord UpdateStage(
        Guid id,
        UpdateModelDeploymentStageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stage = (request.Stage ?? string.Empty).Trim().ToLowerInvariant();
        if (!ModelDeploymentStages.IsValid(stage))
            throw new ModelRegistryValidationException("Deployment stage không hợp lệ.");

        lock (_gate)
        {
            var list = Load();
            var index = list.FindIndex(x => x.Id == id);
            if (index < 0)
                throw new KeyNotFoundException("Không tìm thấy model version.");

            var current = list[index];
            ValidateTransition(current, stage, list);

            if (stage is ModelDeploymentStages.Staging or
                ModelDeploymentStages.Canary or
                ModelDeploymentStages.Production)
            {
                var artifact = artifacts.Get(current.ArtifactId)
                    ?? throw new ModelRegistryValidationException(
                        "Artifact của model version không còn tồn tại.");

                if (artifact.Status != ModelArtifactStatuses.Validated)
                {
                    throw new ModelRegistryValidationException(
                        "Artifact phải ở trạng thái validated trước khi triển khai.");
                }
            }

            list[index] = current with
            {
                Stage = stage,
                Notes = string.IsNullOrWhiteSpace(request.Reason)
                    ? current.Notes
                    : NormalizeOptional(request.Reason, 1000),
                UpdatedAt = DateTimeOffset.UtcNow
            };

            Save(list);
            return list[index];
        }
    }

    private static void ValidateTransition(
        ModelVersionRecord current,
        string next,
        IReadOnlyList<ModelVersionRecord> all)
    {
        if (current.Stage == next) return;

        if (current.Stage == ModelDeploymentStages.Archived)
            throw new ModelRegistryValidationException(
                "Model đã archived không thể chuyển trạng thái.");

        if (next == ModelDeploymentStages.Production &&
            all.Any(x =>
                x.Id != current.Id &&
                x.Family.Equals(current.Family, StringComparison.OrdinalIgnoreCase) &&
                x.Stage == ModelDeploymentStages.Production))
        {
            throw new ModelRegistryConflictException(
                $"Model family '{current.Family}' đã có production version.");
        }

        var allowed = current.Stage switch
        {
            ModelDeploymentStages.Candidate =>
                next is ModelDeploymentStages.Staging or
                    ModelDeploymentStages.Rejected or
                    ModelDeploymentStages.Archived,
            ModelDeploymentStages.Staging =>
                next is ModelDeploymentStages.Canary or
                    ModelDeploymentStages.Rejected or
                    ModelDeploymentStages.Archived,
            ModelDeploymentStages.Canary =>
                next is ModelDeploymentStages.Production or
                    ModelDeploymentStages.Staging or
                    ModelDeploymentStages.Rejected or
                    ModelDeploymentStages.Archived,
            ModelDeploymentStages.Production =>
                next is ModelDeploymentStages.Deprecated,
            ModelDeploymentStages.Deprecated =>
                next is ModelDeploymentStages.Archived,
            ModelDeploymentStages.Rejected =>
                next is ModelDeploymentStages.Archived,
            _ => false
        };

        if (!allowed)
            throw new ModelRegistryValidationException(
                $"Không cho phép chuyển từ '{current.Stage}' sang '{next}'.");
    }

    private List<ModelVersionRecord> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<ModelVersionRecord>>(
                File.ReadAllText(path), Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<ModelVersionRecord> items)
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
        return Path.Combine(_root, $"model-registry-{safe}.json");
    }

    private static IReadOnlyList<string> NormalizeTasks(IReadOnlyList<string>? tasks)
    {
        if (tasks is null || tasks.Count == 0) return [];
        if (tasks.Count > 32)
            throw new ModelRegistryValidationException(
                "CompatibleTasks tối đa 32 phần tử.");

        return tasks
            .Select(x => Normalize(x, "CompatibleTask", 120).ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeFamily(string? value) =>
        Normalize(value, "Family", 120).ToLowerInvariant();

    private static string Normalize(string? value, string field, int max)
    {
        var result = (value ?? string.Empty).Trim();
        if (result.Length is < 1 || result.Length > max)
            throw new ModelRegistryValidationException(
                $"{field} phải có từ 1 đến {max} ký tự.");
        return result;
    }

    private static string? NormalizeOptional(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var result = value.Trim();
        if (result.Length > max)
            throw new ModelRegistryValidationException(
                $"Giá trị tối đa {max} ký tự.");
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
