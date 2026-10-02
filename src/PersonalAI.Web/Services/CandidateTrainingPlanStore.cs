using System.Text.Json;
using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ICandidateTrainingPlanStore
{
    CandidateTrainingStatus GetStatus();
    IReadOnlyList<CandidateTrainingPlan> GetAll();
    CandidateTrainingPlan? Get(Guid id);
    CandidateTrainingPlan? GetByTrainingJob(Guid trainingJobId);
    CandidateTrainingPlan Save(CandidateTrainingPlan plan);
    CandidateTrainingPlan UpdateRegistration(
        Guid trainingJobId,
        Guid artifactId,
        Guid modelVersionId);
    CandidateTrainingPlan Fail(Guid trainingJobId, string reason);
}

public sealed class JsonCandidateTrainingPlanStore(
    IConfiguration configuration,
    IWorkspaceContextAccessor workspace) : ICandidateTrainingPlanStore
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public CandidateTrainingStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.Status == CandidateTrainingStatuses.Queued),
            all.Count(x => x.Status == CandidateTrainingStatuses.CandidateRegistered),
            all.Count(x => x.Status == CandidateTrainingStatuses.Failed),
            VerifiedDataRequired: true,
            ProductionMutationEnabled: false,
            ExplicitConfirmationRequired: true);
    }

    public IReadOnlyList<CandidateTrainingPlan> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public CandidateTrainingPlan? Get(Guid id) =>
        GetAll().FirstOrDefault(x => x.Id == id);

    public CandidateTrainingPlan? GetByTrainingJob(Guid trainingJobId) =>
        GetAll().FirstOrDefault(x => x.TrainingJobId == trainingJobId);

    public CandidateTrainingPlan Save(CandidateTrainingPlan plan)
    {
        lock (_gate)
        {
            var list = Load();
            if (list.Any(x => x.TrainingJobId == plan.TrainingJobId))
                throw new CandidateTrainingValidationException(
                    "TrainingJob đã được liên kết với candidate plan.");

            if (list.Any(x =>
                x.Family.Equals(plan.Family, StringComparison.OrdinalIgnoreCase) &&
                x.Version.Equals(plan.Version, StringComparison.OrdinalIgnoreCase)))
            {
                throw new CandidateTrainingValidationException(
                    $"Candidate family '{plan.Family}' đã có version '{plan.Version}'.");
            }

            list.Add(plan);
            SaveAll(list);
            return plan;
        }
    }

    public CandidateTrainingPlan UpdateRegistration(
        Guid trainingJobId,
        Guid artifactId,
        Guid modelVersionId)
    {
        lock (_gate)
        {
            var list = Load();
            var index = list.FindIndex(x => x.TrainingJobId == trainingJobId);
            if (index < 0)
                throw new KeyNotFoundException("Không tìm thấy candidate training plan.");

            list[index] = list[index] with
            {
                Status = CandidateTrainingStatuses.CandidateRegistered,
                ArtifactId = artifactId,
                ModelVersionId = modelVersionId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            SaveAll(list);
            return list[index];
        }
    }

    public CandidateTrainingPlan Fail(Guid trainingJobId, string reason)
    {
        lock (_gate)
        {
            var list = Load();
            var index = list.FindIndex(x => x.TrainingJobId == trainingJobId);
            if (index < 0)
                throw new KeyNotFoundException("Không tìm thấy candidate training plan.");

            list[index] = list[index] with
            {
                Status = CandidateTrainingStatuses.Failed,
                FailureReason = (reason ?? string.Empty).Trim(),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            SaveAll(list);
            return list[index];
        }
    }

    private List<CandidateTrainingPlan> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<CandidateTrainingPlan>>(
                File.ReadAllText(path), Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void SaveAll(List<CandidateTrainingPlan> plans)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(plans, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_root, $"candidate-training-{safe}.json");
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
        return Path.Combine(root, "Candidates", "Plans");
    }
}
