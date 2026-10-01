using System.Text.Json;
using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IRollbackService
{
    RollbackStatus GetStatus();
    IReadOnlyList<RollbackDecision> GetAll();
    RollbackDecision? Get(Guid id);
    RollbackDecision Evaluate(EvaluateRollbackRequest request);
}

public sealed class RollbackService(
    IModelRolloutService rollouts,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IRollbackService
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public RollbackStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.RollbackPerformed),
            AutomaticRollbackEnabled: true,
            ProductionBaselineMustRemainActive: true,
            AuditRequired: true);
    }

    public IReadOnlyList<RollbackDecision> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public RollbackDecision? Get(Guid id) =>
        GetAll().FirstOrDefault(x => x.Id == id);

    public RollbackDecision Evaluate(EvaluateRollbackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var rollout = rollouts.Get(request.RolloutId)
            ?? throw new KeyNotFoundException("Không tìm thấy rollout.");

        if (rollout.Stage is not (RolloutStages.Staging or RolloutStages.Canary))
            throw new RollbackValidationException(
                "Chỉ rollout staging/canary đang hoạt động mới được đánh giá rollback.");

        var triggers = new[]
        {
            new RollbackTrigger(
                "quality",
                request.QualityScore < request.MinimumQualityScore,
                $"quality={request.QualityScore:F3}; minimum={request.MinimumQualityScore:F3}."),
            new RollbackTrigger(
                "error-rate",
                request.ErrorRate > request.MaximumErrorRate,
                $"errorRate={request.ErrorRate:F3}; maximum={request.MaximumErrorRate:F3}."),
            new RollbackTrigger(
                "latency",
                request.AverageLatencyMilliseconds > request.MaximumLatencyMilliseconds,
                $"latency={request.AverageLatencyMilliseconds:F3}ms; maximum={request.MaximumLatencyMilliseconds:F3}ms."),
            new RollbackTrigger(
                "safety",
                request.SafetyIncidents > request.MaximumSafetyIncidents,
                $"safetyIncidents={request.SafetyIncidents}; maximum={request.MaximumSafetyIncidents}.")
        };

        var required = triggers.Any(x => x.Triggered);
        var resulting = rollout;

        if (required)
        {
            var reason = string.Join(
                "; ",
                triggers.Where(x => x.Triggered).Select(x => $"{x.Metric}:{x.Detail}"));
            resulting = rollouts.Rollback(rollout.Id, reason);
        }

        var decision = new RollbackDecision(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            rollout.Id,
            rollout.CandidateModelVersionId,
            rollout.ProductionModelVersionId,
            triggers,
            required,
            required,
            resulting.Stage,
            resulting.TrafficPercent,
            DateTimeOffset.UtcNow);

        lock (_gate)
        {
            var all = Load();
            all.Add(decision);
            Save(all);
        }

        audit.Record(
            AuditAgents.System,
            "model-lab.rollback.evaluate",
            $"rollback-decision:{decision.Id:D}",
            $"rollout:{rollout.Id:D};required:{required};performed:{decision.RollbackPerformed}",
            AuditResults.Succeeded);

        return decision;
    }

    private static void Validate(EvaluateRollbackRequest request)
    {
        ValidateRate(request.QualityScore, "QualityScore");
        ValidateRate(request.ErrorRate, "ErrorRate");
        ValidateRate(request.MinimumQualityScore, "MinimumQualityScore");
        ValidateRate(request.MaximumErrorRate, "MaximumErrorRate");

        if (!double.IsFinite(request.AverageLatencyMilliseconds) ||
            request.AverageLatencyMilliseconds < 0)
            throw new RollbackValidationException(
                "AverageLatencyMilliseconds phải >= 0.");

        if (!double.IsFinite(request.MaximumLatencyMilliseconds) ||
            request.MaximumLatencyMilliseconds < 0)
            throw new RollbackValidationException(
                "MaximumLatencyMilliseconds phải >= 0.");

        if (request.SafetyIncidents < 0 || request.MaximumSafetyIncidents < 0)
            throw new RollbackValidationException(
                "Safety incident count không được âm.");
    }

    private static void ValidateRate(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
            throw new RollbackValidationException(
                $"{name} phải từ 0 đến 1.");
    }

    private List<RollbackDecision> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<RollbackDecision>>(
                File.ReadAllText(path), Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<RollbackDecision> decisions)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(decisions, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_root, $"rollback-decisions-{safe}.json");
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
        return Path.Combine(root, "Rollbacks");
    }
}
