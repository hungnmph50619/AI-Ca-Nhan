using System.Text.Json;
using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ICandidateTrainingService
{
    CandidateTrainingStatus GetStatus();
    IReadOnlyList<CandidateTrainingPlan> GetAll();
    CandidateTrainingPlan? Get(Guid id);
    CandidateTrainingPlan Start(StartCandidateTrainingRequest request);
}

public sealed class CandidateTrainingService(
    ISyntheticDataService synthetic,
    ISyntheticVerificationService verification,
    IModelLabDatasetStore datasets,
    ITrainingJobStore jobs,
    ICandidateTrainingPlanStore plans,
    IAuditRecorder audit) : ICandidateTrainingService
{
    public CandidateTrainingStatus GetStatus() => plans.GetStatus();
    public IReadOnlyList<CandidateTrainingPlan> GetAll() => plans.GetAll();
    public CandidateTrainingPlan? Get(Guid id) => plans.Get(id);

    public CandidateTrainingPlan Start(StartCandidateTrainingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmStartTraining)
            throw new CandidateTrainingValidationException(
                "Cần ConfirmStartTraining=true để tạo candidate training job.");

        var draft = synthetic.Get(request.DraftId)
            ?? throw new KeyNotFoundException("Không tìm thấy synthetic draft.");

        var verified = verification.GetLatest(draft.Id)
            ?? throw new CandidateTrainingValidationException(
                "Synthetic draft chưa có Verification report.");

        if (!verified.EligibleForTraining ||
            verified.Status != SyntheticVerificationStatuses.Verified)
        {
            throw new CandidateTrainingValidationException(
                "Chỉ synthetic draft VERIFIED mới được dùng để train candidate.");
        }

        var source = datasets.GetVersion(draft.DatasetId, draft.SourceVersion)
            ?? throw new KeyNotFoundException("Không tìm thấy source dataset version.");
        var committed = datasets.GetVersion(draft.DatasetId, request.DatasetVersion)
            ?? throw new KeyNotFoundException(
                "Không tìm thấy committed dataset version để train candidate.");

        if (request.DatasetVersion <= draft.SourceVersion)
            throw new CandidateTrainingValidationException(
                "Candidate training phải dùng dataset version mới hơn source version.");

        EnsureCommittedVersionMatchesVerifiedDraft(source.Items, draft.Items, committed.Items);

        var family = Normalize(request.Family, "Family", 120).ToLowerInvariant();
        var version = Normalize(request.Version, "Version", 80);
        var runtime = string.IsNullOrWhiteSpace(request.Runtime)
            ? "local"
            : Normalize(request.Runtime, "Runtime", 80).ToLowerInvariant();

        var compatibleTasks = (request.CompatibleTasks ?? [])
            .Select(x => Normalize(x, "CompatibleTask", 120).ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToArray();

        var job = jobs.Create(new CreateTrainingJobRequest(
            committed.DatasetId,
            committed.Version,
            request.BaseModel,
            request.TrainingMethod,
            request.Hyperparameters,
            request.Seed,
            committed.ContentSha256));

        var now = DateTimeOffset.UtcNow;
        var plan = new CandidateTrainingPlan(
            Guid.NewGuid(),
            committed.WorkspaceId,
            draft.Id,
            verified.Id,
            committed.DatasetId,
            committed.Version,
            committed.ContentSha256,
            job.Id,
            family,
            version,
            compatibleTasks,
            runtime,
            CandidateTrainingStatuses.Queued,
            null,
            null,
            null,
            now,
            now);

        try
        {
            plans.Save(plan);
        }
        catch
        {
            try { jobs.Cancel(job.Id); }
            catch { }
            throw;
        }

        audit.Record(
            AuditAgents.User,
            "model-lab.candidate-training.start",
            $"candidate-plan:{plan.Id:D}",
            $"verification:{verified.Id:D};training-job:{job.Id:D};family:{family};version:{version}",
            AuditResults.Prepared);

        return plan;
    }

    private static void EnsureCommittedVersionMatchesVerifiedDraft(
        IReadOnlyList<JsonElement> source,
        IReadOnlyList<JsonElement> syntheticItems,
        IReadOnlyList<JsonElement> committed)
    {
        var expected = source
            .Select(x => x.GetRawText())
            .Concat(syntheticItems.Select(x => x.GetRawText()))
            .ToArray();

        if (committed.Count != expected.Length)
            throw new CandidateTrainingValidationException(
                "Committed dataset không khớp số item của source + verified synthetic draft.");

        for (var index = 0; index < expected.Length; index++)
        {
            if (!JsonEquivalent(committed[index], expected[index]))
            {
                throw new CandidateTrainingValidationException(
                    $"Committed dataset không khớp verified draft tại item {index}.");
            }
        }
    }

    private static bool JsonEquivalent(JsonElement left, string rightRaw)
    {
        using var right = JsonDocument.Parse(rightRaw);
        return JsonSerializer.Serialize(left) ==
               JsonSerializer.Serialize(right.RootElement);
    }

    private static string Normalize(string? value, string field, int maximum)
    {
        var result = (value ?? string.Empty).Trim();
        if (result.Length is < 1 || result.Length > maximum)
            throw new CandidateTrainingValidationException(
                $"{field} phải có từ 1 đến {maximum} ký tự.");
        return result;
    }
}
