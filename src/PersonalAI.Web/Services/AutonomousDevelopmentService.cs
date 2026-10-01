using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IAutonomousDevelopmentService
{
    AutonomousDevelopmentStatus GetStatus();
    Task<AutonomousDevelopmentRunResult> RunAsync(
        RunAutonomousDevelopmentRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AutonomousDevelopmentService(
    IAutonomousEmergencyStopService emergencyStop,
    IImprovementBacklogService backlog,
    IRootCauseDiagnosisService diagnoses,
    IDevelopmentRunService runs,
    IDevelopmentRunWorktreeService runWorktrees,
    IAutonomousCodingService coding,
    IDevelopmentAutoTestService tests,
    IDevelopmentReviewService reviews,
    IDevelopmentSecurityService security,
    IDevelopmentBenchmarkService benchmarks,
    IDevelopmentGitHubService github,
    ICiMonitorService ci,
    IDevelopmentMergePolicyService mergePolicy,
    IDevelopmentLocalSyncService localSync,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IAutonomousDevelopmentService
{
    public const int HardMaximumTransitions = 20;

    public AutonomousDevelopmentStatus GetStatus()
    {
        var stop = emergencyStop.Get();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            FullPipelineControllerEnabled: true,
            EmergencyStopActive: stop.Stopped,
            EmergencyStopReason: stop.Reason,
            PolicyBypassAllowed: false,
            MaximumTransitionsPerInvocation: HardMaximumTransitions,
            MaximumCodingEdits: AutonomousCodingService.MaximumEdits,
            MaximumCiRepairAttempts: CiMonitorService.MaximumRepairAttempts,
            DirectMainPushAllowed: false,
            HighRiskAutoMergeAllowed: false,
            [
                "detect",
                "analysis",
                "coding",
                "testing",
                "review",
                "security",
                "benchmark",
                "push",
                "ci",
                "merge-policy",
                "local-sync"
            ]);
    }

    public async Task<AutonomousDevelopmentRunResult> RunAsync(
        RunAutonomousDevelopmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmAutonomousRun)
            throw new AutonomousDevelopmentValidationException(
                "Cần ConfirmAutonomousRun=true.");

        if (request.MaximumTransitions is < 1 or > HardMaximumTransitions)
            throw new AutonomousDevelopmentValidationException(
                $"MaximumTransitions phải từ 1 đến {HardMaximumTransitions}.");

        var baseBranch = NormalizeBaseBranch(request.BaseBranch);
        var startedAt = DateTimeOffset.UtcNow;
        var steps = new List<AutonomousDevelopmentStep>();
        var transitions = 0;

        if (emergencyStop.Get().Stopped)
            return Finish(
                null,
                AutonomousDevelopmentStopReasons.EmergencyStop,
                emergency: true);

        var run = request.DevelopmentRunId is null
            ? DetectEligibleRun(request, baseBranch)
            : runs.Get(request.DevelopmentRunId.Value);

        if (run is null)
            return Finish(
                null,
                AutonomousDevelopmentStopReasons.NoEligibleImprovement);

        while (run.Status == "active" &&
               transitions < request.MaximumTransitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (emergencyStop.Get().Stopped)
                return Finish(
                    run,
                    AutonomousDevelopmentStopReasons.EmergencyStop,
                    emergency: true);

            var stageBefore = run.Stage;

            switch (run.Stage)
            {
                case DevelopmentRunStages.Analysis:
                {
                    if (run.DiagnosisId is null ||
                        !diagnoses.IsDiagnosed(run.DiagnosisId.Value))
                    {
                        return Finish(
                            run,
                            AutonomousDevelopmentStopReasons.AwaitingDiagnosis);
                    }

                    run = runs.Advance(
                        run.Id,
                        new(
                            DevelopmentRunStages.Analysis,
                            "prepared",
                            $"autonomous:diagnosis:{run.DiagnosisId:D}"));

                    AddStep(stageBefore, run.Stage, "verified-diagnosis", "advanced", run.DiagnosisId);
                    transitions++;
                    break;
                }

                case DevelopmentRunStages.Coding:
                {
                    await runWorktrees.EnsureAsync(
                        new(
                            run.Id,
                            baseBranch,
                            ConfirmCreateWorktree: true),
                        cancellationToken);

                    var codingReport = coding.GetLatest(run.Id);
                    if (codingReport is null)
                    {
                        try
                        {
                            codingReport = await coding.RunAsync(
                                run.Id,
                                request.ConfirmExternalAi,
                                cancellationToken);
                        }
                        catch (AutonomousDevelopmentValidationException)
                        {
                            return Finish(
                                run,
                                AutonomousDevelopmentStopReasons.CodingFailed);
                        }
                    }

                    if (emergencyStop.Get().Stopped)
                        return Finish(
                            run,
                            AutonomousDevelopmentStopReasons.EmergencyStop,
                            emergency: true);

                    run = runs.Advance(
                        run.Id,
                        new(
                            DevelopmentRunStages.Coding,
                            "succeeded",
                            $"autonomous:coding-report:{codingReport.Id:D}"));

                    AddStep(stageBefore, run.Stage, "ai-code", "advanced", codingReport.Id);
                    transitions++;
                    break;
                }

                case DevelopmentRunStages.Testing:
                {
                    var report = tests.GetAll()
                        .Where(x => x.DevelopmentRunId == run.Id)
                        .OrderByDescending(x => x.CompletedAt)
                        .FirstOrDefault(x => x.Succeeded);

                    report ??= await tests.RunAsync(
                        new(
                            run.Id,
                            request.DotnetTargetPath,
                            MaximumRegressionCases: request.MaximumRegressionCases,
                            ConfirmExecution: true),
                        cancellationToken);

                    if (!report.Succeeded)
                        return Finish(
                            run,
                            AutonomousDevelopmentStopReasons.GateFailed);

                    if (emergencyStop.Get().Stopped)
                        return Finish(run, AutonomousDevelopmentStopReasons.EmergencyStop, true);

                    run = runs.Advance(
                        run.Id,
                        new(
                            DevelopmentRunStages.Testing,
                            "passed",
                            $"autonomous:test-report:{report.Id:D}"));

                    AddStep(stageBefore, run.Stage, "test", "passed", report.Id);
                    transitions++;
                    break;
                }

                case DevelopmentRunStages.Review:
                {
                    var testReport = tests.GetAll()
                        .Where(x => x.DevelopmentRunId == run.Id && x.Succeeded)
                        .OrderByDescending(x => x.CompletedAt)
                        .FirstOrDefault();

                    if (testReport is null)
                        return Finish(run, AutonomousDevelopmentStopReasons.GateFailed);

                    var report = reviews.GetAll()
                        .Where(x => x.DevelopmentRunId == run.Id && x.Approved)
                        .OrderByDescending(x => x.CreatedAt)
                        .FirstOrDefault();

                    report ??= await reviews.RunAsync(
                        new(
                            run.Id,
                            testReport.Id,
                            CodingAgentId: "autonomous.coder",
                            ReviewerId: ReviewerFrameworkAgent.AgentId,
                            ConfirmReview: true,
                            ConfirmExternalReviewer: request.ConfirmExternalAi),
                        cancellationToken);

                    if (!report.Approved)
                        return Finish(run, AutonomousDevelopmentStopReasons.GateFailed);

                    if (emergencyStop.Get().Stopped)
                        return Finish(run, AutonomousDevelopmentStopReasons.EmergencyStop, true);

                    run = runs.Advance(
                        run.Id,
                        new(
                            DevelopmentRunStages.Review,
                            "approved",
                            $"autonomous:review-report:{report.Id:D}"));

                    AddStep(stageBefore, run.Stage, "independent-review", "approved", report.Id);
                    transitions++;
                    break;
                }

                case DevelopmentRunStages.Security:
                {
                    var reviewReport = reviews.GetAll()
                        .Where(x => x.DevelopmentRunId == run.Id && x.Approved)
                        .OrderByDescending(x => x.CreatedAt)
                        .FirstOrDefault();

                    if (reviewReport is null)
                        return Finish(run, AutonomousDevelopmentStopReasons.GateFailed);

                    var report = security.GetAll()
                        .Where(x =>
                            x.DevelopmentRunId == run.Id &&
                            x.PromotionAllowed &&
                            !x.HighRiskFound)
                        .OrderByDescending(x => x.CreatedAt)
                        .FirstOrDefault();

                    report ??= await security.RunAsync(
                        new(
                            run.Id,
                            reviewReport.Id,
                            ConfirmSecurityReview: true),
                        cancellationToken);

                    if (!report.PromotionAllowed || report.HighRiskFound)
                        return Finish(run, AutonomousDevelopmentStopReasons.GateFailed);

                    if (emergencyStop.Get().Stopped)
                        return Finish(run, AutonomousDevelopmentStopReasons.EmergencyStop, true);

                    run = runs.Advance(
                        run.Id,
                        new(
                            DevelopmentRunStages.Security,
                            "passed",
                            $"autonomous:security-report:{report.Id:D}",
                            SecurityReportId: report.Id));

                    AddStep(stageBefore, run.Stage, "security", "passed", report.Id);
                    transitions++;
                    break;
                }

                case DevelopmentRunStages.Benchmark:
                {
                    var report = benchmarks.GetAll()
                        .Where(x => x.DevelopmentRunId == run.Id && x.Passed)
                        .OrderByDescending(x => x.CompletedAt)
                        .FirstOrDefault();

                    report ??= await benchmarks.RunAsync(
                        new(
                            run.Id,
                            MaximumCases: request.MaximumBenchmarkCases,
                            IncludeAgentBenchmark: true,
                            ConfirmExternalExecution: request.ConfirmExternalAi,
                            ConfirmExecution: true),
                        cancellationToken);

                    if (!report.Passed)
                        return Finish(run, AutonomousDevelopmentStopReasons.GateFailed);

                    if (emergencyStop.Get().Stopped)
                        return Finish(run, AutonomousDevelopmentStopReasons.EmergencyStop, true);

                    run = runs.Advance(
                        run.Id,
                        new(
                            DevelopmentRunStages.Benchmark,
                            "passed",
                            $"autonomous:benchmark-report:{report.Id:D}",
                            BenchmarkReportId: report.Id));

                    AddStep(stageBefore, run.Stage, "benchmark", "passed", report.Id);
                    transitions++;
                    break;
                }

                case DevelopmentRunStages.Push:
                {
                    if (!request.ConfirmGitHubSideEffects ||
                        string.IsNullOrWhiteSpace(request.CredentialRef))
                    {
                        return Finish(
                            run,
                            AutonomousDevelopmentStopReasons.UserConfirmationRequired);
                    }

                    var report = github.GetAll()
                        .Where(x =>
                            x.DevelopmentRunId == run.Id &&
                            x.Pushed &&
                            x.PullRequestCreated)
                        .OrderByDescending(x => x.UpdatedAt)
                        .FirstOrDefault();

                    report ??= await github.PushAndCreatePullRequestAsync(
                        new(
                            run.Id,
                            request.GitHubRepository,
                            baseBranch,
                            $"Autonomous improvement: {run.Goal}",
                            "Created by PersonalAI autonomous development v2.7.15. All downstream gates remain enforced.",
                            request.CredentialRef,
                            ConfirmPush: true,
                            ConfirmCreatePullRequest: true),
                        cancellationToken);

                    if (emergencyStop.Get().Stopped)
                        return Finish(run, AutonomousDevelopmentStopReasons.EmergencyStop, true);

                    run = runs.Advance(
                        run.Id,
                        new(
                            DevelopmentRunStages.Push,
                            "succeeded",
                            $"autonomous:github-report:{report.Id:D}",
                            GitHubReportId: report.Id));

                    AddStep(stageBefore, run.Stage, "push-pr", "succeeded", report.Id);
                    transitions++;
                    break;
                }

                case DevelopmentRunStages.Ci:
                {
                    var githubReport = ResolveGitHubReport(run);
                    if (githubReport is null)
                        return Finish(run, AutonomousDevelopmentStopReasons.GateFailed);

                    var refreshed = github.RefreshCi(githubReport.Id);
                    var ciRun = refreshed.Report.CiRunId is null
                        ? null
                        : ci.Get(refreshed.Report.CiRunId.Value);

                    if (ciRun is null ||
                        ciRun.State != CiMonitorStates.Completed)
                    {
                        return Finish(run, AutonomousDevelopmentStopReasons.CiPending);
                    }

                    if (!string.Equals(
                        ciRun.Conclusion,
                        "success",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        if (ciRun.RepairAttempts >= CiMonitorService.MaximumRepairAttempts)
                            return Finish(run, AutonomousDevelopmentStopReasons.GateFailed);

                        github.RequestRepair(
                            githubReport.Id,
                            new("autonomous-ci-repair-v2.7.15"));

                        AddStep(
                            stageBefore,
                            stageBefore,
                            "ci-repair-request",
                            "requested",
                            ciRun.Id);

                        return Finish(
                            run,
                            AutonomousDevelopmentStopReasons.CiRepairRequested);
                    }

                    run = runs.Advance(
                        run.Id,
                        new(
                            DevelopmentRunStages.Ci,
                            "passed",
                            $"autonomous:ci-run:{ciRun.Id:D}",
                            CiRunId: ciRun.Id));

                    AddStep(stageBefore, run.Stage, "ci", "passed", ciRun.Id);
                    transitions++;
                    break;
                }

                case DevelopmentRunStages.MergePolicy:
                {
                    var reviewReport = reviews.GetAll()
                        .Where(x => x.DevelopmentRunId == run.Id && x.Approved)
                        .OrderByDescending(x => x.CreatedAt)
                        .FirstOrDefault();
                    var securityReport = security.GetAll()
                        .Where(x => x.DevelopmentRunId == run.Id && x.PromotionAllowed && !x.HighRiskFound)
                        .OrderByDescending(x => x.CreatedAt)
                        .FirstOrDefault();
                    var benchmarkReport = benchmarks.GetAll()
                        .Where(x => x.DevelopmentRunId == run.Id && x.Passed)
                        .OrderByDescending(x => x.CompletedAt)
                        .FirstOrDefault();
                    var githubReport = ResolveGitHubReport(run);
                    var ciRun = run.CiRunId is null ? null : ci.Get(run.CiRunId.Value);

                    if (reviewReport is null ||
                        securityReport is null ||
                        benchmarkReport is null ||
                        githubReport is null ||
                        ciRun is null)
                    {
                        return Finish(run, AutonomousDevelopmentStopReasons.GateFailed);
                    }

                    var report = mergePolicy.GetAll()
                        .Where(x => x.DevelopmentRunId == run.Id && x.Merged)
                        .OrderByDescending(x => x.CreatedAt)
                        .FirstOrDefault();

                    report ??= await mergePolicy.EvaluateAndMergeAsync(
                        new(
                            run.Id,
                            githubReport.Id,
                            reviewReport.Id,
                            securityReport.Id,
                            benchmarkReport.Id,
                            ciRun.Id,
                            DevelopmentMergeRiskLevels.Low,
                            "autonomous low-risk backlog item with all required gates passed",
                            request.CredentialRef,
                            AllowAutomaticMerge: request.AllowLowRiskAutomaticMerge,
                            ConfirmNonLowRiskMerge: false),
                        cancellationToken);

                    if (!report.Merged)
                        return Finish(
                            run,
                            AutonomousDevelopmentStopReasons.UserConfirmationRequired);

                    if (emergencyStop.Get().Stopped)
                        return Finish(run, AutonomousDevelopmentStopReasons.EmergencyStop, true);

                    run = runs.Advance(
                        run.Id,
                        new(
                            DevelopmentRunStages.MergePolicy,
                            "approved",
                            $"autonomous:merge-policy:{report.Id:D}",
                            MergePolicyReportId: report.Id));

                    AddStep(stageBefore, run.Stage, "merge-policy", "merged", report.Id);
                    transitions++;
                    break;
                }

                case DevelopmentRunStages.LocalSync:
                {
                    var report = localSync.GetAll()
                        .Where(x =>
                            x.DevelopmentRunId == run.Id &&
                            x.Synced &&
                            !x.DirtyTreeDetected)
                        .OrderByDescending(x => x.CreatedAt)
                        .FirstOrDefault();

                    report ??= await localSync.RunAsync(
                        new(
                            run.Id,
                            baseBranch,
                            CredentialRef: request.CredentialRef,
                            ConfirmSync: true),
                        cancellationToken);

                    if (!report.Synced)
                    {
                        return Finish(
                            run,
                            report.DirtyTreeDetected
                                ? AutonomousDevelopmentStopReasons.DirtyTreeDeferred
                                : AutonomousDevelopmentStopReasons.GateFailed);
                    }

                    if (emergencyStop.Get().Stopped)
                        return Finish(run, AutonomousDevelopmentStopReasons.EmergencyStop, true);

                    run = runs.Advance(
                        run.Id,
                        new(
                            DevelopmentRunStages.LocalSync,
                            "succeeded",
                            $"autonomous:local-sync:{report.Id:D}",
                            LocalSyncReportId: report.Id));

                    AddStep(stageBefore, run.Stage, "local-sync", "synced", report.Id);
                    transitions++;
                    break;
                }

                default:
                    return Finish(
                        run,
                        run.Status == "completed"
                            ? AutonomousDevelopmentStopReasons.Completed
                            : AutonomousDevelopmentStopReasons.GateFailed);
            }

            run = runs.Get(run.Id) ?? run;
        }

        if (run.Status == "completed")
        {
            if (run.ImprovementItemId is not null)
            {
                backlog.UpdateState(
                    run.ImprovementItemId.Value,
                    new(
                        ImprovementBacklogStates.Resolved,
                        "autonomous-development-v2.7.15-completed"));
            }

            return Finish(run, AutonomousDevelopmentStopReasons.Completed);
        }

        return Finish(
            run,
            AutonomousDevelopmentStopReasons.TransitionBudgetReached);

        void AddStep(
            string before,
            string after,
            string action,
            string result,
            Guid? evidenceId)
        {
            steps.Add(new(
                before,
                after,
                action,
                result,
                evidenceId,
                DateTimeOffset.UtcNow));
        }

        AutonomousDevelopmentRunResult Finish(
            DevelopmentRun? current,
            string reason,
            bool emergency = false)
        {
            var result = new AutonomousDevelopmentRunResult(
                PersonalAiRelease.Version,
                workspace.CurrentWorkspaceId,
                current?.Id,
                current?.Stage,
                current?.Status,
                transitions,
                current?.Status == "completed",
                emergency,
                PolicyBypassed: false,
                reason,
                steps.ToArray(),
                startedAt,
                DateTimeOffset.UtcNow);

            audit.Record(
                AuditAgents.System,
                "development.autonomous.run",
                current is null
                    ? "development-run:none"
                    : $"development-run:{current.Id:D}",
                $"transitions:{transitions};completed:{result.Completed};emergency:{emergency};policy-bypassed:false;stop:{reason}",
                result.Completed
                    ? AuditResults.Succeeded
                    : AuditResults.Prepared);

            return result;
        }
    }

    private DevelopmentRun? DetectEligibleRun(
        RunAutonomousDevelopmentRequest request,
        string baseBranch)
    {
        var activeIds = runs.GetAll()
            .Where(x =>
                x.Status == "active" &&
                x.ImprovementItemId is not null)
            .Select(x => x.ImprovementItemId!.Value)
            .ToHashSet();

        foreach (var item in backlog.GetAll()
            .Where(x =>
                x.Priority == ImprovementPriorities.Low &&
                (x.State is ImprovementBacklogStates.Open or
                    ImprovementBacklogStates.Planned) &&
                !activeIds.Contains(x.Id))
            .OrderByDescending(x => x.Occurrences)
            .ThenBy(x => x.FirstSeenAt))
        {
            var diagnosis = diagnoses.GetAll()
                .Where(x =>
                    x.ImprovementItemId == item.Id &&
                    diagnoses.IsDiagnosed(x.Id))
                .OrderByDescending(x => x.DiagnosedAt)
                .FirstOrDefault();

            if (diagnosis is null)
                continue;

            var branch =
                $"experiment/autonomous/{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{item.Id:N}";

            var run = runs.Create(
                new(
                    $"Autonomous low-risk improvement: {item.Title}",
                    request.RepositoryPath,
                    branch,
                    RoadmapVersion: "autonomous-v2.7.15",
                    ConfirmCreate: true,
                    ImprovementItemId: item.Id,
                    DiagnosisId: diagnosis.Id));

            backlog.UpdateState(
                item.Id,
                new(
                    ImprovementBacklogStates.InProgress,
                    "selected-by-autonomous-development-v2.7.15"));

            return run;
        }

        return null;
    }

    private DevelopmentGitHubReport? ResolveGitHubReport(DevelopmentRun run)
    {
        if (run.GitHubReportId is not null)
        {
            var linked = github.Get(run.GitHubReportId.Value);
            if (linked is not null)
                return linked;
        }

        return github.GetAll()
            .Where(x =>
                x.DevelopmentRunId == run.Id &&
                x.Pushed &&
                x.PullRequestCreated)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefault();
    }

    private static string NormalizeBaseBranch(string? value)
    {
        var branch = (value ?? string.Empty).Trim();
        if (branch is not ("main" or "master"))
            throw new AutonomousDevelopmentValidationException(
                "BaseBranch phải là main hoặc master.");
        return branch;
    }
}
