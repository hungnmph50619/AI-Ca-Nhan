using System.Text;
using PersonalAI.Web.Models;
using PersonalAI.Web.SelfImprovement;

namespace PersonalAI.Web.Services;

public interface IAutomatedReviewService
{
    Task<AutomatedReviewResult> RunAsync(
        RunAutomatedReviewRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AutomatedReviewService(
    IDevelopmentAgentService development,
    IAgentFrameworkService agents,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IAutomatedReviewService
{
    public const int MaximumReviewMaterialCharacters = 3_800;
    public const int MaximumDiffCharacters = 2_600;
    public const int MaximumProcessExcerptCharacters = 500;

    public async Task<AutomatedReviewResult> RunAsync(
        RunAutomatedReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Experiment);
        ArgumentNullException.ThrowIfNull(request.SelfCoding);

        ValidateRequest(request);

        var startedAt = DateTimeOffset.UtcNow;
        var checks = new List<AutomatedReviewCheck>();

        var currentBranch = await development.GetCurrentBranchAsync(
            request.Experiment.RepositoryPath,
            cancellationToken);

        if (!currentBranch.Succeeded ||
            !string.Equals(
                currentBranch.Branch,
                request.Experiment.ExperimentBranch,
                StringComparison.Ordinal))
        {
            throw new AutomatedReviewValidationException(
                "Automated Review chỉ chạy khi repository đang ở đúng experiment branch.");
        }

        checks.Add(new AutomatedReviewCheck(
            "experiment-branch",
            "passed",
            $"Đang ở branch {currentBranch.Branch}."));

        var diff = await development.GitDiffAsync(
            request.Experiment.RepositoryPath,
            staged: false,
            cancellationToken);

        if (!diff.Succeeded)
            throw new AutomatedReviewValidationException(
                "Không đọc được Git diff của experiment branch.");

        if (string.IsNullOrWhiteSpace(diff.Output))
            throw new AutomatedReviewValidationException(
                "Experiment branch không có working-tree diff để review.");

        checks.Add(new AutomatedReviewCheck(
            "git-diff",
            "passed",
            "Đã đọc working-tree diff; không stage/commit thay đổi."));

        var build = await development.DotnetBuildAsync(
            request.DotnetTargetPath,
            request.Configuration,
            cancellationToken);

        checks.Add(new AutomatedReviewCheck(
            "dotnet-build",
            build.Succeeded ? "passed" : "failed",
            BuildProcessDetail(build)));

        DevelopmentProcessResult? tests = null;
        if (request.RunTests && build.Succeeded)
        {
            tests = await development.DotnetTestAsync(
                request.DotnetTargetPath,
                request.Configuration,
                cancellationToken);
            checks.Add(new AutomatedReviewCheck(
                "dotnet-test",
                tests.Succeeded ? "passed" : "failed",
                BuildProcessDetail(tests)));
        }
        else if (request.RunTests)
        {
            checks.Add(new AutomatedReviewCheck(
                "dotnet-test",
                "skipped",
                "Build không đạt nên không chạy test."));
        }

        ReviewerReport? reviewerReport = null;
        string? reviewerProvider = null;
        string? reviewerModel = null;

        if (request.ConfirmExternalReviewer)
        {
            var material = BuildReviewMaterial(
                request,
                diff,
                build,
                tests);

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

            reviewerReport = response.Reviewer
                ?? throw new AutomatedReviewValidationException(
                    "Reviewer Agent không trả ReviewerReport.");
            reviewerProvider = response.Provider;
            reviewerModel = response.Model;

            checks.Add(new AutomatedReviewCheck(
                "reviewer-agent",
                reviewerReport.Findings.Count == 0 ? "passed" : "needs-review",
                reviewerReport.Findings.Count == 0
                    ? "Reviewer không tìm thấy finding có evidence excerpt trong material đã cung cấp."
                    : $"Reviewer ghi nhận {reviewerReport.Findings.Count} finding cần người dùng xem lại."));
        }
        else
        {
            checks.Add(new AutomatedReviewCheck(
                "reviewer-agent",
                "skipped",
                "Chưa xác nhận gọi AI provider cho Reviewer Agent."));
        }

        var buildPassed = build.Succeeded;
        var testsPassed = !request.RunTests || (tests?.Succeeded ?? false);
        var reviewerClear =
            reviewerReport is null || reviewerReport.Findings.Count == 0;

        var readyForHumanDecision =
            buildPassed && testsPassed && reviewerClear;
        var decisionStatus = readyForHumanDecision
            ? AutomatedReviewDecisionStatuses.ReadyForHumanReview
            : AutomatedReviewDecisionStatuses.ChangesRequired;

        audit.Record(
            AuditAgents.System,
            "self-improvement.automated-review",
            $"experiment:{request.Experiment.ExperimentId}",
            "build-test-diff-review",
            readyForHumanDecision
                ? AuditResults.Prepared
                : AuditResults.Failed);

        return new AutomatedReviewResult(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            request.Experiment.ExperimentId,
            request.Experiment.ExperimentBranch,
            startedAt,
            DateTimeOffset.UtcNow,
            checks,
            build,
            tests,
            reviewerReport,
            reviewerProvider,
            reviewerModel,
            decisionStatus,
            readyForHumanDecision,
            Approved: false,
            CommitCreated: false,
            Pushed: false,
            Merged: false,
            Deployed: false);
    }

    private void ValidateRequest(RunAutomatedReviewRequest request)
    {
        if (!string.Equals(
            request.Experiment.WorkspaceId,
            workspace.CurrentWorkspaceId,
            StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                request.SelfCoding.WorkspaceId,
                workspace.CurrentWorkspaceId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new AutomatedReviewValidationException(
                "Experiment/Self Coding result không thuộc workspace hiện tại.");
        }

        if (!string.Equals(
            request.Experiment.ExperimentId,
            request.SelfCoding.ExperimentId,
            StringComparison.Ordinal) ||
            !string.Equals(
                request.Experiment.ExperimentBranch,
                request.SelfCoding.Branch,
                StringComparison.Ordinal))
        {
            throw new AutomatedReviewValidationException(
                "Self Coding result không khớp experiment.");
        }

        if (!request.Experiment.ExperimentBranch.StartsWith(
            "experiment/",
            StringComparison.Ordinal))
        {
            throw new AutomatedReviewValidationException(
                "Automated Review chỉ nhận experiment/* branch.");
        }

        if (request.SelfCoding.FilesChanged < 1 ||
            request.SelfCoding.CommitCreated ||
            request.SelfCoding.Pushed ||
            request.SelfCoding.Merged)
        {
            throw new AutomatedReviewValidationException(
                "Self Coding result không hợp lệ cho review trước commit.");
        }

        if (!request.ConfirmCodeExecution)
        {
            throw new AutomatedReviewValidationException(
                "Chạy build/test trên source code cần xác nhận rõ của người dùng.");
        }

        if (string.IsNullOrWhiteSpace(request.DotnetTargetPath))
        {
            throw new AutomatedReviewValidationException(
                "Cần chỉ định project/solution để build/test.");
        }
    }

    private static string BuildReviewMaterial(
        RunAutomatedReviewRequest request,
        DevelopmentGitResult diff,
        DevelopmentProcessResult build,
        DevelopmentProcessResult? tests)
    {
        var builder = new StringBuilder();
        builder.AppendLine("AUTOMATED REVIEW v2.4.4 — MATERIAL ONLY");
        builder.Append("Experiment: ").AppendLine(request.Experiment.ExperimentId);
        builder.Append("Branch: ").AppendLine(request.Experiment.ExperimentBranch);
        builder.Append("Files changed: ").AppendLine(request.SelfCoding.FilesChanged.ToString());
        builder.AppendLine("Reviewer must review only the supplied diff and execution summaries; do not approve or infer external facts.");
        builder.AppendLine();
        builder.AppendLine("GIT DIFF:");
        builder.AppendLine(Limit(diff.Output, MaximumDiffCharacters));
        builder.AppendLine();
        builder.AppendLine("BUILD:");
        builder.AppendLine(BuildProcessDetail(build));
        builder.AppendLine(Limit(build.Output, MaximumProcessExcerptCharacters));

        if (tests is not null)
        {
            builder.AppendLine();
            builder.AppendLine("TEST:");
            builder.AppendLine(BuildProcessDetail(tests));
            builder.AppendLine(Limit(tests.Output, MaximumProcessExcerptCharacters));
        }

        var value = builder.ToString();
        return value.Length <= MaximumReviewMaterialCharacters
            ? value
            : value[..MaximumReviewMaterialCharacters];
    }

    private static string BuildProcessDetail(DevelopmentProcessResult result) =>
        $"{result.Tool}: succeeded={result.Succeeded}, exitCode={result.ExitCode}, timedOut={result.TimedOut}, durationMs={result.DurationMs}, outputTruncated={result.OutputTruncated}";

    private static string Limit(string? value, int maximum)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length <= maximum ? text : text[..maximum];
    }
}
