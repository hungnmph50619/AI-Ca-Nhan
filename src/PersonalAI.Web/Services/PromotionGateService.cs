using System.Text.Json;
using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IPromotionGateService
{
    PromotionGateStatus GetStatus();
    IReadOnlyList<PromotionGateDecision> GetAll();
    PromotionGateDecision? Get(Guid id);
    PromotionGateDecision Evaluate(EvaluatePromotionGateRequest request);
}

public sealed class PromotionGateService(
    IRegisteredModelComparisonService comparisons,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IPromotionGateService
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PromotionGateStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.Decision == PromotionGateDecisions.ApprovedForStaging),
            all.Count(x => x.Decision == PromotionGateDecisions.Blocked),
            CriticalRegressionAlwaysBlocks: true,
            AutoPromotionEnabled: false,
            AuditRequired: true);
    }

    public IReadOnlyList<PromotionGateDecision> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public PromotionGateDecision? Get(Guid id) =>
        GetAll().FirstOrDefault(x => x.Id == id);

    public PromotionGateDecision Evaluate(EvaluatePromotionGateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var comparison = comparisons.Get(request.ComparisonReportId)
            ?? throw new KeyNotFoundException("Không tìm thấy comparison report.");

        ValidateThresholds(request);

        var checks = new List<PromotionGateCheck>();

        checks.Add(new(
            "critical-regression",
            !request.CriticalRegression,
            Critical: true,
            request.CriticalRegression
                ? "Phát hiện critical regression: luôn chặn promotion."
                : "Không có critical regression."));

        var regressionPass =
            comparison.Candidate.RegressionPassRate >= request.MinimumRegressionPassRate &&
            comparison.DeltaCandidateMinusProduction.RegressionPassRate >= 0;
        checks.Add(new(
            "regression",
            regressionPass,
            Critical: true,
            $"candidate={comparison.Candidate.RegressionPassRate:F3}; delta={comparison.DeltaCandidateMinusProduction.RegressionPassRate:F3}; minimum={request.MinimumRegressionPassRate:F3}."));

        checks.Add(new(
            "security",
            request.SecurityPassed,
            Critical: true,
            request.SecurityPassed
                ? "Security gate PASS."
                : "Security gate FAIL."));

        checks.Add(new(
            "lineage",
            request.LineagePassed,
            Critical: true,
            request.LineagePassed
                ? "Lineage gate PASS."
                : "Lineage gate FAIL."));

        var latencyPass =
            comparison.DeltaCandidateMinusProduction.AverageLatencyMilliseconds <=
            request.MaximumLatencyIncreaseMilliseconds;
        checks.Add(new(
            "latency",
            latencyPass,
            Critical: false,
            $"latency delta={comparison.DeltaCandidateMinusProduction.AverageLatencyMilliseconds:F3}ms; max increase={request.MaximumLatencyIncreaseMilliseconds:F3}ms."));

        var costPass =
            comparison.DeltaCandidateMinusProduction.AverageCostPerCase <=
            request.MaximumCostIncreasePerCase;
        checks.Add(new(
            "cost",
            costPass,
            Critical: false,
            $"cost delta={comparison.DeltaCandidateMinusProduction.AverageCostPerCase:F6}; max increase={request.MaximumCostIncreasePerCase:F6}."));

        var resourceIncrease = request.CandidateResourceRatio - 1d;
        var resourcePass = resourceIncrease <= request.MaximumResourceIncreaseRatio;
        checks.Add(new(
            "resource",
            resourcePass,
            Critical: false,
            $"candidate resource ratio={request.CandidateResourceRatio:F3}; max increase ratio={request.MaximumResourceIncreaseRatio:F3}."));

        var eligible = checks.All(x => x.Passed);
        var decision = eligible
            ? PromotionGateDecisions.ApprovedForStaging
            : PromotionGateDecisions.Blocked;

        var result = new PromotionGateDecision(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            comparison.Id,
            comparison.CandidateModelVersionId,
            comparison.ProductionModelVersionId,
            comparison.Family,
            checks,
            decision,
            eligible,
            ProductionMutationPerformed: false,
            DateTimeOffset.UtcNow);

        lock (_gate)
        {
            var all = Load();
            all.Add(result);
            Save(all);
        }

        audit.Record(
            AuditAgents.User,
            "model-lab.promotion-gate.evaluate",
            $"promotion-gate:{result.Id:D}",
            $"comparison:{comparison.Id:D};decision:{decision};candidate:{comparison.CandidateModelVersionId:D}",
            eligible ? AuditResults.Succeeded : AuditResults.Failed);

        return result;
    }

    private static void ValidateThresholds(EvaluatePromotionGateRequest request)
    {
        if (!double.IsFinite(request.MaximumLatencyIncreaseMilliseconds) ||
            request.MaximumLatencyIncreaseMilliseconds < 0)
            throw new PromotionGateValidationException(
                "MaximumLatencyIncreaseMilliseconds phải >= 0.");

        if (!double.IsFinite(request.MaximumCostIncreasePerCase) ||
            request.MaximumCostIncreasePerCase < 0)
            throw new PromotionGateValidationException(
                "MaximumCostIncreasePerCase phải >= 0.");

        if (!double.IsFinite(request.MinimumRegressionPassRate) ||
            request.MinimumRegressionPassRate is < 0 or > 1)
            throw new PromotionGateValidationException(
                "MinimumRegressionPassRate phải từ 0 đến 1.");

        if (!double.IsFinite(request.MaximumResourceIncreaseRatio) ||
            request.MaximumResourceIncreaseRatio < 0)
            throw new PromotionGateValidationException(
                "MaximumResourceIncreaseRatio phải >= 0.");

        if (!double.IsFinite(request.CandidateResourceRatio) ||
            request.CandidateResourceRatio <= 0)
            throw new PromotionGateValidationException(
                "CandidateResourceRatio phải > 0.");
    }

    private List<PromotionGateDecision> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<PromotionGateDecision>>(
                File.ReadAllText(path), Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<PromotionGateDecision> decisions)
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
        return Path.Combine(_root, $"promotion-gates-{safe}.json");
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
        return Path.Combine(root, "PromotionGates");
    }
}
