using System.Text;
using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentReviewService
{
    DevelopmentReviewStatus GetStatus();
    IReadOnlyList<DevelopmentReviewReport> GetAll();
    DevelopmentReviewReport? Get(Guid id);
    Task<DevelopmentReviewReport> RunAsync(
        RunDevelopmentReviewRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DevelopmentReviewService(
    IDevelopmentRunService runs,
    IDevelopmentAutoTestService tests,
    ILocalGitRepositoryService git,
    IAgentFrameworkService agents,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IDevelopmentReviewService
{
    public const int MaximumReviewMaterialCharacters = 8_000;
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public DevelopmentReviewStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.Approved),
            all.Count(x => !x.Approved),
            ReportsPersisted: true,
            IndependentReviewerRequired: true,
            DeveloperSelfApprovalAllowed: false,
            ["correctness", "architecture", "regression"]);
    }

    public IReadOnlyList<DevelopmentReviewReport> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public DevelopmentReviewReport? Get(Guid id)
    {
        lock (_gate)
            return Load().FirstOrDefault(x => x.Id == id);
    }

    public async Task<DevelopmentReviewReport> RunAsync(
        RunDevelopmentReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmReview)
            throw new DevelopmentReviewValidationException(
                "Cần ConfirmReview=true để chạy independent code review.");

        if (!request.ConfirmExternalReviewer)
            throw new DevelopmentReviewValidationException(
                "Cần ConfirmExternalReviewer=true để gọi Reviewer Agent.");

        var codingAgentId = NormalizeAgentId(request.CodingAgentId, "CodingAgentId");
        var reviewerId = NormalizeAgentId(request.ReviewerId, "ReviewerId");

        if (codingAgentId.Equals(reviewerId, StringComparison.OrdinalIgnoreCase))
            throw new DevelopmentReviewValidationException(
                "Coding agent không được tự review/approve code của chính mình.");

        if (!reviewerId.Equals(
                ReviewerFrameworkAgent.AgentId,
                StringComparison.Ordinal))
        {
            throw new DevelopmentReviewValidationException(
                $"ReviewerId phải là '{ReviewerFrameworkAgent.AgentId}' ở v2.7.5.");
        }

        var run = runs.Get(request.DevelopmentRunId)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

        if (run.WorkspaceId != workspace.CurrentWorkspaceId)
            throw new DevelopmentReviewValidationException(
                "DevelopmentRun không thuộc workspace hiện tại.");

        if (run.Status != "active" || run.Stage != DevelopmentRunStages.Review)
            throw new DevelopmentReviewValidationException(
                "Development review chỉ chạy khi DevelopmentRun đang ở stage review.");

        var testReport = tests.Get(request.TestReportId)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentTestReport.");

        if (testReport.DevelopmentRunId != run.Id ||
            !testReport.Branch.Equals(run.Branch, StringComparison.Ordinal) ||
            !testReport.WorktreePath.Contains(
                DevelopmentWorktreeService.AgentWorktreeDirectory,
                StringComparison.Ordinal))
        {
            throw new DevelopmentReviewValidationException(
                "Test report không thuộc đúng DevelopmentRun/worktree.");
        }

        var checks = new List<DevelopmentReviewCheck>();

        checks.Add(new(
            "regression",
            testReport.Succeeded ? "passed" : "failed",
            testReport.Succeeded
                ? "Development test report đã pass."
                : "Development test report có phase thất bại."));

        var diff = await git.DiffAsync(
            new LocalGitDiffRequest(
                testReport.WorktreePath,
                Staged: false),
            cancellationToken);

        if (!diff.Succeeded)
            throw new DevelopmentReviewValidationException(
                "Không đọc được diff của isolated worktree.");

        if (string.IsNullOrWhiteSpace(diff.Output))
            throw new DevelopmentReviewValidationException(
                "Không có working-tree diff để reviewer đánh giá.");

        var material = BuildMaterial(run, testReport, diff.Output);

        var response = await agents.ExecuteAsync(
            ReviewerFrameworkAgent.AgentId,
            new AgentExecutionRequest(
                material,
                Conversation: null,
                UseKnowledge: false,
                UseMemory: false,
                UseTaskContext: false,
                UseLifeContext: false),
            cancellationToken);

        var reviewer = response.Reviewer
            ?? throw new DevelopmentReviewValidationException(
                "Reviewer Agent không trả ReviewerReport.");

        var correctnessPassed =
            reviewer.Findings.Count == 0 &&
            reviewer.EvidenceExcerptsValidated;

        var architecturePassed =
            reviewer.Findings.All(x =>
                !x.Observation.Contains("architecture", StringComparison.OrdinalIgnoreCase) &&
                !x.Observation.Contains("coupling", StringComparison.OrdinalIgnoreCase));

        checks.Add(new(
            "correctness",
            correctnessPassed ? "passed" : "changes-required",
            correctnessPassed
                ? "Reviewer không tìm thấy correctness finding có evidence."
                : $"Reviewer có {reviewer.Findings.Count} finding hoặc evidence chưa hợp lệ."));

        checks.Add(new(
            "architecture",
            architecturePassed ? "passed" : "changes-required",
            architecturePassed
                ? "Không có architecture/coupling finding trong reviewer report."
                : "Reviewer phát hiện architecture/coupling concern."));

        var approved =
            testReport.Succeeded &&
            correctnessPassed &&
            architecturePassed &&
            !reviewer.Approved &&
            !reviewer.ModifiedContent &&
            !reviewer.ExecutedTools &&
            !reviewer.DispatchedAgents;

        var report = new DevelopmentReviewReport(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            run.Id,
            testReport.Id,
            run.RepositoryPath,
            run.Branch,
            testReport.WorktreePath,
            codingAgentId,
            reviewerId,
            checks,
            approved
                ? DevelopmentReviewStatuses.Approved
                : DevelopmentReviewStatuses.ChangesRequired,
            approved,
            IndependentReviewer: true,
            DateTimeOffset.UtcNow);

        lock (_gate)
        {
            var all = Load();
            all.Add(report);
            Save(all);
        }

        audit.Record(
            AuditAgents.System,
            "development.review.complete",
            $"development-run:{run.Id:D}",
            $"report:{report.Id:D};coding-agent:{codingAgentId};reviewer:{reviewerId};approved:{approved};findings:{reviewer.Findings.Count}",
            approved ? AuditResults.Succeeded : AuditResults.Failed);

        return report;
    }

    private static string BuildMaterial(
        DevelopmentRun run,
        DevelopmentTestReport test,
        string diff)
    {
        var builder = new StringBuilder();
        builder.AppendLine("INDEPENDENT DEVELOPMENT REVIEW v2.7.5");
        builder.Append("DevelopmentRun: ").AppendLine(run.Id.ToString("D"));
        builder.Append("Branch: ").AppendLine(run.Branch);
        builder.Append("TestReport: ").AppendLine(test.Id.ToString("D"));
        builder.Append("TestsPassed: ").AppendLine(test.Succeeded.ToString());
        builder.AppendLine("Review correctness, architecture and regression risk using only supplied material.");
        builder.AppendLine("Do not modify code, execute tools, dispatch agents or approve as the coding agent.");
        builder.AppendLine();
        builder.AppendLine("DIFF:");
        builder.AppendLine(diff);

        var value = builder.ToString();
        return value.Length <= MaximumReviewMaterialCharacters
            ? value
            : value[..MaximumReviewMaterialCharacters];
    }

    private static string NormalizeAgentId(string? value, string name)
    {
        var id = (value ?? string.Empty).Trim();
        if (id.Length is < 1 or > 120 ||
            id.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            throw new DevelopmentReviewValidationException(
                $"{name} không hợp lệ.");
        }
        return id;
    }

    private List<DevelopmentReviewReport> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<DevelopmentReviewReport>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<DevelopmentReviewReport> reports)
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
        return Path.Combine(_root, $"development-review-reports-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["Development:ReviewRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "Reviews");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }
}
