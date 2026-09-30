using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IRegisteredModelComparisonService
{
    RegisteredModelComparisonStatus GetStatus();
    IReadOnlyList<RegisteredModelComparisonReport> GetAll();
    RegisteredModelComparisonReport? Get(Guid id);
    RegisteredModelComparisonReport Compare(CompareRegisteredModelsRequest request);
}

public sealed class RegisteredModelComparisonService(
    IModelRegistry registry,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IRegisteredModelComparisonService
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public RegisteredModelComparisonStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            ProductionBaselineRequired: true,
            SameBenchmarkRequired: true,
            AutoPromotionEnabled: false,
            Persistent: true);
    }

    public IReadOnlyList<RegisteredModelComparisonReport> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public RegisteredModelComparisonReport? Get(Guid id) =>
        GetAll().FirstOrDefault(x => x.Id == id);

    public RegisteredModelComparisonReport Compare(
        CompareRegisteredModelsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.CandidateMetrics);
        ArgumentNullException.ThrowIfNull(request.ProductionMetrics);

        var candidate = registry.Get(request.CandidateModelVersionId)
            ?? throw new KeyNotFoundException("Không tìm thấy candidate model version.");

        ModelVersionRecord production;
        if (request.ProductionModelVersionId is Guid productionId)
        {
            production = registry.Get(productionId)
                ?? throw new KeyNotFoundException("Không tìm thấy production model version.");
        }
        else
        {
            production = registry.GetFamily(candidate.Family)
                .SingleOrDefault(x => x.Stage == ModelDeploymentStages.Production)
                ?? throw new RegisteredModelComparisonValidationException(
                    $"Model family '{candidate.Family}' chưa có production version.");
        }

        if (!candidate.WorkspaceId.Equals(
                workspace.CurrentWorkspaceId,
                StringComparison.OrdinalIgnoreCase) ||
            !production.WorkspaceId.Equals(
                workspace.CurrentWorkspaceId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new RegisteredModelComparisonValidationException(
                "Model version không thuộc workspace hiện tại.");
        }

        if (!candidate.Family.Equals(
                production.Family,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new RegisteredModelComparisonValidationException(
                "Candidate và production phải thuộc cùng model family.");
        }

        if (production.Stage != ModelDeploymentStages.Production)
            throw new RegisteredModelComparisonValidationException(
                "Baseline phải ở stage production.");

        if (candidate.Stage == ModelDeploymentStages.Production)
            throw new RegisteredModelComparisonValidationException(
                "Candidate không được ở stage production.");

        if (candidate.Id == production.Id)
            throw new RegisteredModelComparisonValidationException(
                "Candidate và production không được là cùng một model version.");

        ValidateMetrics(request.CandidateMetrics, "candidate");
        ValidateMetrics(request.ProductionMetrics, "production");

        if (!request.CandidateMetrics.BenchmarkId.Equals(
                request.ProductionMetrics.BenchmarkId,
                StringComparison.Ordinal) ||
            !request.CandidateMetrics.BenchmarkSha256.Equals(
                request.ProductionMetrics.BenchmarkSha256,
                StringComparison.OrdinalIgnoreCase) ||
            request.CandidateMetrics.Cases != request.ProductionMetrics.Cases)
        {
            throw new RegisteredModelComparisonValidationException(
                "Candidate và production phải được đo trên cùng benchmark ID/checksum/số case.");
        }

        var canonical = JsonSerializer.Serialize(new
        {
            workspaceId = workspace.CurrentWorkspaceId,
            family = candidate.Family,
            candidate = new
            {
                candidate.Id,
                candidate.Version,
                candidate.ArtifactSha256,
                request.CandidateMetrics
            },
            production = new
            {
                production.Id,
                production.Version,
                production.ArtifactSha256,
                request.ProductionMetrics
            }
        });

        var delta = new RegisteredModelMetricDelta(
            request.CandidateMetrics.Accuracy -
                request.ProductionMetrics.Accuracy,
            request.CandidateMetrics.TaskSuccessRate -
                request.ProductionMetrics.TaskSuccessRate,
            request.CandidateMetrics.AverageLatencyMilliseconds -
                request.ProductionMetrics.AverageLatencyMilliseconds,
            request.CandidateMetrics.AverageCostPerCase -
                request.ProductionMetrics.AverageCostPerCase,
            request.CandidateMetrics.RegressionPassRate -
                request.ProductionMetrics.RegressionPassRate);

        var report = new RegisteredModelComparisonReport(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            candidate.Family,
            candidate.Id,
            candidate.Version,
            production.Id,
            production.Version,
            request.CandidateMetrics,
            request.ProductionMetrics,
            delta,
            Sha256(canonical),
            ReproducibleInputs: true,
            ProductionMutationPerformed: false,
            DateTimeOffset.UtcNow);

        lock (_gate)
        {
            var all = Load();
            all.Add(report);
            Save(all);
        }

        audit.Record(
            AuditAgents.User,
            "model-lab.registered-model.compare",
            $"model-comparison:{report.Id:D}",
            $"family:{report.Family};candidate:{candidate.Id:D};production:{production.Id:D};benchmark:{request.CandidateMetrics.BenchmarkId}",
            AuditResults.Succeeded);

        return report;
    }

    private static void ValidateMetrics(
        RegisteredModelBenchmarkMetrics metrics,
        string label)
    {
        var benchmarkId = (metrics.BenchmarkId ?? string.Empty).Trim();
        var sha = (metrics.BenchmarkSha256 ?? string.Empty).Trim().ToLowerInvariant();

        if (benchmarkId.Length is < 1 or > 160)
            throw new RegisteredModelComparisonValidationException(
                $"BenchmarkId của {label} không hợp lệ.");
        if (sha.Length != 64 || sha.Any(c => !Uri.IsHexDigit(c)))
            throw new RegisteredModelComparisonValidationException(
                $"BenchmarkSha256 của {label} phải có đúng 64 ký tự hex.");
        if (metrics.Cases < 1 || metrics.Cases > 100_000)
            throw new RegisteredModelComparisonValidationException(
                $"Cases của {label} không hợp lệ.");

        ValidateRate(metrics.Accuracy, "Accuracy", label);
        ValidateRate(metrics.TaskSuccessRate, "TaskSuccessRate", label);
        ValidateRate(metrics.RegressionPassRate, "RegressionPassRate", label);

        if (!double.IsFinite(metrics.AverageLatencyMilliseconds) ||
            metrics.AverageLatencyMilliseconds < 0)
        {
            throw new RegisteredModelComparisonValidationException(
                $"AverageLatencyMilliseconds của {label} không hợp lệ.");
        }

        if (!double.IsFinite(metrics.AverageCostPerCase) ||
            metrics.AverageCostPerCase < 0)
        {
            throw new RegisteredModelComparisonValidationException(
                $"AverageCostPerCase của {label} không hợp lệ.");
        }
    }

    private static void ValidateRate(
        double value,
        string metric,
        string label)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new RegisteredModelComparisonValidationException(
                $"{metric} của {label} phải từ 0 đến 1.");
        }
    }

    private List<RegisteredModelComparisonReport> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<RegisteredModelComparisonReport>>(
                File.ReadAllText(path), Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<RegisteredModelComparisonReport> reports)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(reports, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_root, $"registered-model-comparisons-{safe}.json");
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
        return Path.Combine(root, "Comparisons");
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();
}
