using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IRootCauseDiagnosisService
{
    RootCauseDiagnosisStatus GetStatus();
    IReadOnlyList<RootCauseDiagnosis> GetAll();
    RootCauseDiagnosis? Get(Guid id);
    RootCauseDiagnosis Start(StartRootCauseDiagnosisRequest request);
    RootCauseDiagnosis RecordReproduction(Guid id, RecordReproductionRequest request);
    RootCauseDiagnosis ProposeHypothesis(Guid id, ProposeRootCauseHypothesisRequest request);
    RootCauseDiagnosis VerifyHypothesis(Guid id, VerifyRootCauseHypothesisRequest request);
    bool IsDiagnosed(Guid id);
}

public sealed class RootCauseDiagnosisService(
    IImprovementBacklogService backlog,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IRootCauseDiagnosisService
{
    public const int MaximumHypotheses = 12;

    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public RootCauseDiagnosisStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.Stage == RootCauseDiagnosisStages.Diagnosed),
            all.Count(x => x.Stage != RootCauseDiagnosisStages.Diagnosed),
            SourceEditAllowedBeforeDiagnosis: false,
            EvidenceRequired: true,
            ReproductionRequired: true,
            HypothesisVerificationRequired: true,
            Persisted: true);
    }

    public IReadOnlyList<RootCauseDiagnosis> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.UpdatedAt).ToArray();
    }

    public RootCauseDiagnosis? Get(Guid id)
    {
        lock (_gate)
            return Load().FirstOrDefault(x => x.Id == id);
    }

    public bool IsDiagnosed(Guid id)
    {
        var item = Get(id);
        return item is not null &&
               item.Stage == RootCauseDiagnosisStages.Diagnosed &&
               item.VerifiedHypothesisId is not null;
    }

    public RootCauseDiagnosis Start(StartRootCauseDiagnosisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmStart)
            throw new RootCauseDiagnosisValidationException(
                "Cần ConfirmStart=true để bắt đầu chẩn đoán nguyên nhân gốc.");

        var repository = NormalizePath(request.RepositoryPath);
        var improvement = backlog.Get(request.ImprovementItemId)
            ?? throw new KeyNotFoundException("Không tìm thấy improvement backlog item.");

        if (improvement.Evidence.Count == 0)
            throw new RootCauseDiagnosisValidationException(
                "Backlog item chưa có evidence để chẩn đoán.");

        lock (_gate)
        {
            var all = Load();
            var active = all.FirstOrDefault(x =>
                x.ImprovementItemId == request.ImprovementItemId &&
                x.Stage != RootCauseDiagnosisStages.Diagnosed);

            if (active is not null)
                throw new RootCauseDiagnosisConflictException(
                    "Backlog item đã có root-cause diagnosis đang hoạt động.");

            var now = DateTimeOffset.UtcNow;
            var diagnosis = new RootCauseDiagnosis(
                Guid.NewGuid(),
                workspace.CurrentWorkspaceId,
                improvement.Id,
                repository,
                RootCauseDiagnosisStages.EvidenceCollected,
                improvement.Evidence.ToArray(),
                null,
                null,
                null,
                [],
                null,
                now,
                now,
                null);

            all.Add(diagnosis);
            Save(all);

            backlog.UpdateState(
                improvement.Id,
                new UpdateImprovementBacklogStateRequest(
                    ImprovementBacklogStates.InProgress,
                    "Root-cause diagnosis started."));

            audit.Record(
                AuditAgents.System,
                "improvement.diagnosis.start",
                $"diagnosis:{diagnosis.Id:D}",
                $"improvement:{improvement.Id:D};evidence:{diagnosis.SourceEvidence.Count}",
                AuditResults.Prepared);

            return diagnosis;
        }
    }

    public RootCauseDiagnosis RecordReproduction(
        Guid id,
        RecordReproductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var steps = NormalizeText(request.Steps, 3, 4000, "Steps");
        var result = NormalizeText(request.ObservedResult, 3, 4000, "ObservedResult");
        var reference = NormalizeText(
            request.EvidenceReference, 1, 500, "EvidenceReference");

        if (!request.Reproduced)
            throw new RootCauseDiagnosisValidationException(
                "Chưa tái hiện được lỗi; chưa được chuyển sang bước giả thuyết.");

        lock (_gate)
        {
            var all = Load();
            var index = FindIndex(all, id);
            var current = all[index];

            if (current.Stage != RootCauseDiagnosisStages.EvidenceCollected)
                throw new RootCauseDiagnosisConflictException(
                    $"Diagnosis đang ở stage '{current.Stage}', không thể record reproduction.");

            var now = DateTimeOffset.UtcNow;
            all[index] = current with
            {
                Stage = RootCauseDiagnosisStages.Reproduced,
                ReproductionSteps = steps,
                ReproductionResult = result,
                ReproductionEvidenceReference = reference,
                UpdatedAt = now
            };
            Save(all);

            audit.Record(
                AuditAgents.System,
                "improvement.diagnosis.reproduce",
                $"diagnosis:{id:D}",
                $"reproduced:true;reference:{SafeInline(reference, 200)}",
                AuditResults.Succeeded);

            return all[index];
        }
    }

    public RootCauseDiagnosis ProposeHypothesis(
        Guid id,
        ProposeRootCauseHypothesisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var statement = NormalizeText(request.Hypothesis, 3, 4000, "Hypothesis");

        var refs = (request.EvidenceReferences ?? [])
            .Select(x => NormalizeText(x, 1, 500, "EvidenceReference"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (refs.Length == 0)
            throw new RootCauseDiagnosisValidationException(
                "Hypothesis phải tham chiếu ít nhất một evidence.");

        lock (_gate)
        {
            var all = Load();
            var index = FindIndex(all, id);
            var current = all[index];

            if (current.Stage is not (
                RootCauseDiagnosisStages.Reproduced or
                RootCauseDiagnosisStages.HypothesisProposed))
            {
                throw new RootCauseDiagnosisConflictException(
                    $"Diagnosis đang ở stage '{current.Stage}', chưa thể tạo giả thuyết.");
            }

            if (current.Hypotheses.Count >= MaximumHypotheses)
                throw new RootCauseDiagnosisValidationException(
                    $"Diagnosis đã đạt giới hạn {MaximumHypotheses} giả thuyết.");

            var knownRefs = current.SourceEvidence
                .Select(x => x.Reference)
                .Append(current.ReproductionEvidenceReference ?? string.Empty)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (refs.Any(x => !knownRefs.Contains(x)))
                throw new RootCauseDiagnosisValidationException(
                    "Hypothesis tham chiếu evidence chưa tồn tại trong diagnosis.");

            var hypothesis = new RootCauseHypothesis(
                Guid.NewGuid(),
                statement,
                refs,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                null);

            var hypotheses = current.Hypotheses
                .Concat([hypothesis])
                .ToArray();

            all[index] = current with
            {
                Stage = RootCauseDiagnosisStages.HypothesisProposed,
                Hypotheses = hypotheses,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Save(all);

            audit.Record(
                AuditAgents.System,
                "improvement.diagnosis.hypothesis",
                $"diagnosis:{id:D}",
                $"hypothesis:{hypothesis.Id:D};evidence:{refs.Length}",
                AuditResults.Prepared);

            return all[index];
        }
    }

    public RootCauseDiagnosis VerifyHypothesis(
        Guid id,
        VerifyRootCauseHypothesisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var method = NormalizeText(
            request.VerificationMethod, 3, 2000, "VerificationMethod");
        var result = NormalizeText(
            request.Result, 3, 4000, "Result");
        var reference = NormalizeText(
            request.EvidenceReference, 1, 500, "EvidenceReference");

        lock (_gate)
        {
            var all = Load();
            var index = FindIndex(all, id);
            var current = all[index];

            if (current.Stage != RootCauseDiagnosisStages.HypothesisProposed)
                throw new RootCauseDiagnosisConflictException(
                    $"Diagnosis đang ở stage '{current.Stage}', chưa thể verify hypothesis.");

            if (current.Hypotheses.Count == 0)
                throw new RootCauseDiagnosisValidationException(
                    "Diagnosis chưa có giả thuyết.");

            var hypothesis = current.Hypotheses[^1];
            var verifiedAt = DateTimeOffset.UtcNow;

            var updatedHypothesis = hypothesis with
            {
                Verified = request.Verified,
                VerificationMethod = method,
                VerificationResult = result,
                VerifiedAt = verifiedAt
            };

            var hypotheses = current.Hypotheses
                .Take(current.Hypotheses.Count - 1)
                .Concat([updatedHypothesis])
                .ToArray();

            var nextStage = request.Verified
                ? RootCauseDiagnosisStages.Diagnosed
                : RootCauseDiagnosisStages.Reproduced;

            all[index] = current with
            {
                Stage = nextStage,
                Hypotheses = hypotheses,
                VerifiedHypothesisId = request.Verified ? hypothesis.Id : null,
                UpdatedAt = verifiedAt,
                DiagnosedAt = request.Verified ? verifiedAt : null
            };
            Save(all);

            audit.Record(
                AuditAgents.System,
                "improvement.diagnosis.verify",
                $"diagnosis:{id:D}",
                $"hypothesis:{hypothesis.Id:D};verified:{request.Verified};reference:{SafeInline(reference, 200)}",
                request.Verified ? AuditResults.Succeeded : AuditResults.Failed);

            return all[index];
        }
    }

    private static int FindIndex(List<RootCauseDiagnosis> all, Guid id)
    {
        var index = all.FindIndex(x => x.Id == id);
        if (index < 0)
            throw new KeyNotFoundException("Không tìm thấy root-cause diagnosis.");
        return index;
    }

    private static string NormalizePath(string? value)
    {
        var path = (value ?? string.Empty).Trim().Replace('\\', '/');
        if (path.Length > 300 ||
            Path.IsPathRooted(path) ||
            path.StartsWith("../", StringComparison.Ordinal) ||
            path.Contains("/../", StringComparison.Ordinal))
        {
            throw new RootCauseDiagnosisValidationException(
                "RepositoryPath không hợp lệ.");
        }
        return path;
    }

    private static string NormalizeText(
        string? value,
        int minimum,
        int maximum,
        string name)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length < minimum || text.Length > maximum)
            throw new RootCauseDiagnosisValidationException(
                $"{name} phải có từ {minimum} đến {maximum} ký tự.");
        return text;
    }

    private List<RootCauseDiagnosis> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<RootCauseDiagnosis>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<RootCauseDiagnosis> items)
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
        return Path.Combine(_root, $"root-cause-diagnoses-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["Development:DiagnosisRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "Diagnosis");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string SafeInline(string value, int maximum)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }
}
