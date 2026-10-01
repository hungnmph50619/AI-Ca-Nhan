using System.Text.Json;
using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IModelRolloutService
{
    ModelRolloutStatus GetStatus();
    IReadOnlyList<ModelRolloutState> GetAll();
    ModelRolloutState? Get(Guid id);
    ModelRolloutState Start(StartModelRolloutRequest request);
    ModelRolloutState Advance(Guid rolloutId, AdvanceModelRolloutRequest request);
    ModelRolloutState Rollback(Guid rolloutId, string reason);
}

public sealed class ModelRolloutService(
    IPromotionGateService gates,
    IModelRegistry registry,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IModelRolloutService
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ModelRolloutStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.Stage == RolloutStages.Staging),
            all.Count(x => x.Stage == RolloutStages.Canary),
            all.Count(x => x.Stage == RolloutStages.Completed),
            all.Count(x => x.Stage == RolloutStages.Blocked),
            all.Count(x => x.Stage == RolloutStages.RolledBack),
            DirectProductionPromotionEnabled: false,
            Persisted: true,
            ExplicitConfirmationRequired: true);
    }

    public IReadOnlyList<ModelRolloutState> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public ModelRolloutState? Get(Guid id) =>
        GetAll().FirstOrDefault(x => x.Id == id);

    public ModelRolloutState Start(StartModelRolloutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmStartRollout)
            throw new ModelRolloutValidationException(
                "Cần ConfirmStartRollout=true để bắt đầu rollout.");
        if (request.StagingTrafficPercent != 0)
            throw new ModelRolloutValidationException(
                "StagingTrafficPercent ở v2.6.6 phải bằng 0; staging chưa nhận production traffic.");

        var gate = gates.Get(request.PromotionGateDecisionId)
            ?? throw new KeyNotFoundException("Không tìm thấy Promotion Gate decision.");

        if (!gate.EligibleForStaging ||
            gate.Decision != PromotionGateDecisions.ApprovedForStaging)
        {
            throw new ModelRolloutValidationException(
                "Promotion Gate chưa approved-for-staging.");
        }

        var candidate = registry.Get(gate.CandidateModelVersionId)
            ?? throw new KeyNotFoundException("Không tìm thấy candidate model version.");
        var production = registry.Get(gate.ProductionModelVersionId)
            ?? throw new KeyNotFoundException("Không tìm thấy production model version.");

        if (candidate.Stage != ModelDeploymentStages.Candidate)
            throw new ModelRolloutValidationException(
                "Candidate phải đang ở stage candidate khi bắt đầu rollout.");
        if (production.Stage != ModelDeploymentStages.Production)
            throw new ModelRolloutValidationException(
                "Production baseline phải đang ở stage production.");

        lock (_gate)
        {
            var existing = Load();
            if (existing.Any(x =>
                x.CandidateModelVersionId == candidate.Id &&
                x.Stage is RolloutStages.Staging or RolloutStages.Canary))
            {
                throw new ModelRolloutValidationException(
                    "Candidate đã có rollout đang hoạt động.");
            }
        }

        var staged = registry.UpdateStage(
            candidate.Id,
            new UpdateModelDeploymentStageRequest(
                ModelDeploymentStages.Staging,
                $"rollout gate:{gate.Id:D}"));

        var now = DateTimeOffset.UtcNow;
        var rollout = new ModelRolloutState(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            gate.Id,
            staged.Id,
            production.Id,
            gate.Family,
            RolloutStages.Staging,
            TrafficPercent: 0,
            ProductionMutationPerformed: false,
            now,
            now);

        lock (_gate)
        {
            var all = Load();
            all.Add(rollout);
            Save(all);
        }

        audit.Record(
            AuditAgents.User,
            "model-lab.rollout.start",
            $"rollout:{rollout.Id:D}",
            $"candidate:{candidate.Id:D};gate:{gate.Id:D};stage:staging",
            AuditResults.Succeeded);

        return rollout;
    }

    public ModelRolloutState Advance(
        Guid rolloutId,
        AdvanceModelRolloutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmAdvance)
            throw new ModelRolloutValidationException(
                "Cần ConfirmAdvance=true để chuyển rollout stage.");

        var target = (request.TargetStage ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (target == ModelDeploymentStages.Production ||
            target == RolloutStages.Completed)
        {
            throw new ModelRolloutValidationException(
                "v2.6.6 không cho phép chuyển rollout trực tiếp lên production/completed.");
        }

        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x => x.Id == rolloutId);
            if (index < 0)
                throw new KeyNotFoundException("Không tìm thấy rollout.");

            var current = all[index];
            if (current.Stage != RolloutStages.Staging ||
                target != RolloutStages.Canary)
            {
                throw new ModelRolloutValidationException(
                    $"Chỉ cho phép staging → canary ở v2.6.6. Hiện tại='{current.Stage}', target='{target}'.");
            }

            var traffic = request.CanaryTrafficPercent
                ?? throw new ModelRolloutValidationException(
                    "CanaryTrafficPercent là bắt buộc khi chuyển sang canary.");

            if (traffic is < 1 or > 25)
                throw new ModelRolloutValidationException(
                    "CanaryTrafficPercent phải từ 1 đến 25.");

            var model = registry.Get(current.CandidateModelVersionId)
                ?? throw new KeyNotFoundException("Không tìm thấy candidate model version.");

            if (model.Stage != ModelDeploymentStages.Staging)
                throw new ModelRolloutValidationException(
                    "Model phải đang ở staging trước khi chuyển canary.");

            registry.UpdateStage(
                model.Id,
                new UpdateModelDeploymentStageRequest(
                    ModelDeploymentStages.Canary,
                    $"rollout:{current.Id:D};traffic:{traffic}%"));

            all[index] = current with
            {
                Stage = RolloutStages.Canary,
                TrafficPercent = traffic,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Save(all);

            audit.Record(
                AuditAgents.User,
                "model-lab.rollout.advance",
                $"rollout:{current.Id:D}",
                $"stage:canary;traffic:{traffic}%",
                AuditResults.Succeeded);

            return all[index];
        }
    }

    public ModelRolloutState Rollback(Guid rolloutId, string reason)
    {
        var normalizedReason = (reason ?? string.Empty).Trim();
        if (normalizedReason.Length is < 1 or > 1000)
            throw new ModelRolloutValidationException(
                "Rollback reason phải có từ 1 đến 1000 ký tự.");

        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x => x.Id == rolloutId);
            if (index < 0)
                throw new KeyNotFoundException("Không tìm thấy rollout.");

            var current = all[index];
            if (current.Stage is not (RolloutStages.Staging or RolloutStages.Canary))
                throw new ModelRolloutValidationException(
                    "Chỉ rollout staging/canary đang hoạt động mới có thể rollback.");

            var candidate = registry.Get(current.CandidateModelVersionId)
                ?? throw new KeyNotFoundException("Không tìm thấy candidate model version.");
            var production = registry.Get(current.ProductionModelVersionId)
                ?? throw new KeyNotFoundException("Không tìm thấy production baseline.");

            if (production.Stage != ModelDeploymentStages.Production)
                throw new ModelRolloutValidationException(
                    "Production baseline không còn ở stage production.");

            if (candidate.Stage is not (ModelDeploymentStages.Staging or ModelDeploymentStages.Canary))
                throw new ModelRolloutValidationException(
                    "Candidate không còn ở staging/canary.");

            registry.UpdateStage(
                candidate.Id,
                new UpdateModelDeploymentStageRequest(
                    ModelDeploymentStages.Rejected,
                    $"rollback:{current.Id:D};reason:{normalizedReason}"));

            all[index] = current with
            {
                Stage = RolloutStages.RolledBack,
                TrafficPercent = 0,
                ProductionMutationPerformed = false,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Save(all);

            audit.Record(
                AuditAgents.System,
                "model-lab.rollout.rollback",
                $"rollout:{current.Id:D}",
                $"candidate:{candidate.Id:D};production:{production.Id:D};reason:{normalizedReason}",
                AuditResults.Succeeded);

            return all[index];
        }
    }

    private List<ModelRolloutState> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<ModelRolloutState>>(
                File.ReadAllText(path), Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<ModelRolloutState> states)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(states, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_root, $"rollouts-{safe}.json");
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
        return Path.Combine(root, "Rollouts");
    }
}
