using System.Text.Json;
using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ISyntheticVerificationService
{
    SyntheticVerificationStatus GetStatus();
    IReadOnlyList<SyntheticVerificationReport> GetAll();
    SyntheticVerificationReport? GetLatest(Guid draftId);
    SyntheticVerificationReport Verify(
        SyntheticDataDraft draft,
        VerifySyntheticDraftRequest request);
}

public sealed class SyntheticVerificationService(
    IModelLabDatasetStore datasets,
    ISyntheticCriticService critic,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : ISyntheticVerificationService
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SyntheticVerificationStatus GetStatus()
    {
        var reports = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            reports.Count,
            reports.Count(x => x.EligibleForTraining),
            CriticRequired: true,
            DatasetValidationRequired: true,
            EvaluationGateRequired: true,
            VerificationRequiredForCommit: true);
    }

    public IReadOnlyList<SyntheticVerificationReport> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public SyntheticVerificationReport? GetLatest(Guid draftId) =>
        GetAll()
            .Where(x => x.DraftId == draftId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();

    public SyntheticVerificationReport Verify(
        SyntheticDataDraft draft,
        VerifySyntheticDraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(request);

        if (request.DraftId != draft.Id)
            throw new SyntheticVerificationException(
                "DraftId không khớp synthetic draft cần xác minh.");
        if (request.MinimumCriticAverage is < 0.5 or > 1)
            throw new SyntheticVerificationException(
                "MinimumCriticAverage phải từ 0.5 đến 1.");
        if (request.MinimumSafetyAverage is < 0.5 or > 1)
            throw new SyntheticVerificationException(
                "MinimumSafetyAverage phải từ 0.5 đến 1.");
        if (request.MaximumSyntheticRatio is <= 0 or > 1)
            throw new SyntheticVerificationException(
                "MaximumSyntheticRatio phải > 0 và <= 1.");

        var layers = new List<VerificationLayerResult>();
        var source = datasets.GetVersion(draft.DatasetId, draft.SourceVersion)
            ?? throw new KeyNotFoundException("Không tìm thấy dataset version nguồn.");

        var sourceImmutable =
            source.ContentSha256.Equals(
                draft.SourceSha256,
                StringComparison.OrdinalIgnoreCase) &&
            source.WorkspaceId.Equals(
                workspace.CurrentWorkspaceId,
                StringComparison.OrdinalIgnoreCase);

        layers.Add(new(
            "source-integrity",
            sourceImmutable,
            sourceImmutable
                ? "Source dataset version và checksum khớp draft."
                : "Source dataset hoặc checksum không còn khớp draft."));

        var criticReport = critic.GetLatest(draft.Id)
            ?? throw new SyntheticVerificationException(
                "Synthetic draft chưa có Critic report.");

        var criticPassed =
            criticReport.SemanticReviewed &&
            criticReport.Passed &&
            criticReport.Items == draft.Items.Count;

        layers.Add(new(
            "critic",
            criticPassed,
            criticPassed
                ? "Semantic Critic đã PASS toàn bộ item."
                : "Semantic Critic chưa PASS hoặc số item không khớp."));

        var criticAverage = criticReport.Reviews.Count == 0
            ? 0d
            : criticReport.Reviews.Average(x => x.Overall);
        var safetyAverage = criticReport.Reviews.Count == 0
            ? 0d
            : criticReport.Reviews.Average(x => x.Safety);

        var prospective = source.Items
            .Select(x => x.Clone())
            .Concat(draft.Items.Select(x => x.Clone()))
            .ToArray();

        var datasetCheck = ValidateProspectiveDataset(prospective);
        layers.Add(new(
            "dataset-validation",
            datasetCheck.Passed,
            datasetCheck.Detail,
            datasetCheck.Score));

        var denominator = Math.Max(1, prospective.Length);
        var syntheticRatio = (double)draft.Items.Count / denominator;

        var evaluationPassed =
            criticAverage >= request.MinimumCriticAverage &&
            safetyAverage >= request.MinimumSafetyAverage &&
            syntheticRatio <= request.MaximumSyntheticRatio &&
            draft.Items.Count > 0;

        layers.Add(new(
            "evaluation-gate",
            evaluationPassed,
            $"criticAverage={criticAverage:F3}; safetyAverage={safetyAverage:F3}; syntheticRatio={syntheticRatio:F3}.",
            criticAverage));

        var eligible =
            sourceImmutable &&
            criticPassed &&
            datasetCheck.Passed &&
            evaluationPassed;

        var report = new SyntheticVerificationReport(
            Guid.NewGuid(),
            draft.Id,
            workspace.CurrentWorkspaceId,
            draft.DatasetId,
            draft.SourceVersion,
            draft.SourceSha256,
            criticReport.Id,
            layers,
            criticAverage,
            safetyAverage,
            syntheticRatio,
            datasetCheck.Passed,
            evaluationPassed,
            eligible,
            DateTimeOffset.UtcNow);

        lock (_gate)
        {
            var all = Load();
            all.Add(report);
            Save(all);
        }

        audit.Record(
            AuditAgents.System,
            "model-lab.synthetic.verify",
            $"synthetic-draft:{draft.Id:D}",
            $"eligible:{eligible};critic:{criticAverage:F3};safety:{safetyAverage:F3};ratio:{syntheticRatio:F3}",
            eligible ? AuditResults.Succeeded : AuditResults.Failed);

        return report;
    }

    private static (bool Passed, string Detail, double Score)
        ValidateProspectiveDataset(IReadOnlyList<JsonElement> items)
    {
        if (items.Count == 0)
            return (false, "Dataset dự kiến không có item.", 0);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var valid = 0;

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("input", out var input) ||
                input.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(input.GetString()) ||
                !item.TryGetProperty("expected", out var expected) ||
                expected.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(expected.GetString()))
            {
                return (
                    false,
                    $"Item {index} không đạt schema input + expected.",
                    (double)valid / items.Count);
            }

            var key =
                $"{input.GetString()!.Trim().ToLowerInvariant()}\n{expected.GetString()!.Trim().ToLowerInvariant()}";
            if (!seen.Add(key))
            {
                return (
                    false,
                    $"Dataset dự kiến có duplicate tại item {index}.",
                    (double)valid / items.Count);
            }

            var raw = item.GetRawText().ToLowerInvariant();
            if (raw.Contains("-----begin private key-----") ||
                raw.Contains("bearer ") ||
                raw.Contains("\"api_key\"") ||
                raw.Contains("\"password\"") ||
                raw.Contains("sk-"))
            {
                return (
                    false,
                    $"Dataset dự kiến có dấu hiệu secret tại item {index}.",
                    (double)valid / items.Count);
            }

            valid++;
        }

        return (
            true,
            $"Dataset dự kiến hợp lệ: {items.Count} item, không duplicate/secret/schema error.",
            1d);
    }

    private List<SyntheticVerificationReport> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<SyntheticVerificationReport>>(
                File.ReadAllText(path),
                JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<SyntheticVerificationReport> reports)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(reports, JsonOptions));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

        return Path.Combine(_root, $"verification-reports-{safe}.json");
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
        return Path.Combine(root, "Synthetic", "Verification");
    }
}
