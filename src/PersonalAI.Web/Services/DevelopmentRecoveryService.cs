using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentRecoveryService
{
    DevelopmentRecoveryStatus GetStatus();
    DevelopmentRecoveryResult Recover(RecoverDevelopmentRunRequest request);
}

public sealed class DevelopmentRecoveryService(
    IDevelopmentRunService runs,
    IDevelopmentAutoTestService tests,
    IDevelopmentReviewService reviews,
    IDevelopmentSecurityReportStore security,
    IDevelopmentBenchmarkReportStore benchmarks,
    IDevelopmentGitHubReportStore github,
    ICiMonitorService ci,
    IDevelopmentMergePolicyReportStore mergePolicy,
    IDevelopmentLocalSyncReportStore localSync,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IDevelopmentRecoveryService
{
    private static readonly IReadOnlyList<string> ReconciliableStages =
    [
        DevelopmentRunStages.Testing,
        DevelopmentRunStages.Review,
        DevelopmentRunStages.Security,
        DevelopmentRunStages.Benchmark,
        DevelopmentRunStages.Push,
        DevelopmentRunStages.Ci,
        DevelopmentRunStages.MergePolicy,
        DevelopmentRunStages.LocalSync
    ];

    public DevelopmentRecoveryStatus GetStatus()
    {
        var active = runs.GetAll()
            .Where(x => x.Status == "active")
            .ToArray();

        var needingRecovery = active.Count(CanReconcileCurrentStage);

        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            active.Length,
            needingRecovery,
            PersistedStateUsed: true,
            SideEffectReplayEnabled: false,
            SafeReconciliationEnabled: true,
            ReconciliableStages);
    }

    public DevelopmentRecoveryResult Recover(
        RecoverDevelopmentRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmRecovery)
            throw new DevelopmentRecoveryValidationException(
                "Cần ConfirmRecovery=true để reconcile DevelopmentRun sau restart.");

        var run = runs.Get(request.DevelopmentRunId)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

        if (run.WorkspaceId != workspace.CurrentWorkspaceId)
            throw new DevelopmentRecoveryValidationException(
                "DevelopmentRun không thuộc workspace hiện tại.");

        if (run.Status != "active")
            throw new DevelopmentRecoveryValidationException(
                "Chỉ DevelopmentRun active mới cần recovery.");

        var stageBefore = run.Stage;
        var evidence = new List<DevelopmentRecoveryEvidence>();
        var advance = BuildAdvanceRequest(run, evidence);

        if (advance is null)
        {
            var result = new DevelopmentRecoveryResult(
                PersonalAiRelease.Version,
                workspace.CurrentWorkspaceId,
                run.Id,
                stageBefore,
                stageBefore,
                Advanced: false,
                SideEffectReplayed: false,
                RecoveryRequired: false,
                NextAction: NextActionFor(stageBefore),
                evidence,
                DateTimeOffset.UtcNow);

            audit.Record(
                AuditAgents.System,
                "development.recovery.inspect",
                $"development-run:{run.Id:D}",
                $"stage:{stageBefore};advance:false;side-effect-replayed:false",
                AuditResults.Prepared);

            return result;
        }

        var advanced = runs.Advance(run.Id, advance);

        var output = new DevelopmentRecoveryResult(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            run.Id,
            stageBefore,
            advanced.Stage,
            Advanced: true,
            SideEffectReplayed: false,
            RecoveryRequired:
                advanced.Status == "active" &&
                CanReconcileCurrentStage(advanced),
            NextAction:
                advanced.Status == "completed"
                    ? "completed"
                    : CanReconcileCurrentStage(advanced)
                        ? "reconcile-next-persisted-stage"
                        : NextActionFor(advanced.Stage),
            evidence,
            DateTimeOffset.UtcNow);

        audit.Record(
            AuditAgents.System,
            "development.recovery.reconcile",
            $"development-run:{run.Id:D}",
            $"from:{stageBefore};to:{advanced.Stage};side-effect-replayed:false;evidence:{evidence.Count}",
            advanced.Status == "completed"
                ? AuditResults.Succeeded
                : AuditResults.Prepared);

        return output;
    }

    private AdvanceDevelopmentRunRequest? BuildAdvanceRequest(
        DevelopmentRun run,
        List<DevelopmentRecoveryEvidence> evidence)
    {
        switch (run.Stage)
        {
            case DevelopmentRunStages.Testing:
            {
                var report = tests.GetAll()
                    .Where(x => x.DevelopmentRunId == run.Id)
                    .OrderByDescending(x => x.CompletedAt)
                    .FirstOrDefault(x => x.Succeeded);

                if (report is null)
                    return null;

                evidence.Add(new(
                    run.Stage,
                    "development-test-report",
                    report.Id,
                    true,
                    "Persisted test report succeeded; test side effect is not replayed."));

                return new(
                    run.Stage,
                    "passed",
                    $"recovery:test-report:{report.Id:D}");
            }

            case DevelopmentRunStages.Review:
            {
                var report = reviews.GetAll()
                    .Where(x => x.DevelopmentRunId == run.Id)
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefault(x => x.Approved);

                if (report is null)
                    return null;

                evidence.Add(new(
                    run.Stage,
                    "development-review-report",
                    report.Id,
                    true,
                    "Persisted independent review approved; review side effect is not replayed."));

                return new(
                    run.Stage,
                    "approved",
                    $"recovery:review-report:{report.Id:D}");
            }

            case DevelopmentRunStages.Security:
            {
                var report = security.GetAll()
                    .Where(x => x.DevelopmentRunId == run.Id)
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefault(x =>
                        x.PromotionAllowed &&
                        !x.HighRiskFound);

                if (report is null)
                    return null;

                evidence.Add(new(
                    run.Stage,
                    "development-security-report",
                    report.Id,
                    true,
                    "Persisted security report passed; security scan is not replayed."));

                return new(
                    run.Stage,
                    "passed",
                    $"recovery:security-report:{report.Id:D}",
                    SecurityReportId: report.Id);
            }

            case DevelopmentRunStages.Benchmark:
            {
                var report = benchmarks.GetAll()
                    .Where(x => x.DevelopmentRunId == run.Id)
                    .OrderByDescending(x => x.CompletedAt)
                    .FirstOrDefault(x => x.Passed);

                if (report is null)
                    return null;

                evidence.Add(new(
                    run.Stage,
                    "development-benchmark-report",
                    report.Id,
                    true,
                    "Persisted benchmark report passed; benchmark execution is not replayed."));

                return new(
                    run.Stage,
                    "passed",
                    $"recovery:benchmark-report:{report.Id:D}",
                    BenchmarkReportId: report.Id);
            }

            case DevelopmentRunStages.Push:
            {
                var report = github.GetAll()
                    .Where(x => x.DevelopmentRunId == run.Id)
                    .OrderByDescending(x => x.UpdatedAt)
                    .FirstOrDefault(x =>
                        x.Pushed &&
                        x.PullRequestCreated &&
                        x.Branch.Equals(
                            run.Branch,
                            StringComparison.Ordinal));

                if (report is null)
                    return null;

                evidence.Add(new(
                    run.Stage,
                    "development-github-report",
                    report.Id,
                    true,
                    "Persisted push/PR report found; push and PR creation are not replayed."));

                return new(
                    run.Stage,
                    "succeeded",
                    $"recovery:github-report:{report.Id:D}",
                    GitHubReportId: report.Id);
            }

            case DevelopmentRunStages.Ci:
            {
                var ciRun = ResolveSuccessfulCi(run);
                if (ciRun is null)
                    return null;

                evidence.Add(new(
                    run.Stage,
                    "ci-run",
                    ciRun.Id,
                    true,
                    "Persisted CI run completed successfully; CI is not re-triggered."));

                return new(
                    run.Stage,
                    "passed",
                    $"recovery:ci-run:{ciRun.Id:D}",
                    CiRunId: ciRun.Id);
            }

            case DevelopmentRunStages.MergePolicy:
            {
                var report = mergePolicy.GetAll()
                    .Where(x => x.DevelopmentRunId == run.Id)
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefault(x => x.Merged);

                if (report is null)
                    return null;

                evidence.Add(new(
                    run.Stage,
                    "development-merge-policy-report",
                    report.Id,
                    true,
                    "Persisted merged report found; merge API is not replayed."));

                return new(
                    run.Stage,
                    "approved",
                    $"recovery:merge-policy-report:{report.Id:D}",
                    MergePolicyReportId: report.Id);
            }

            case DevelopmentRunStages.LocalSync:
            {
                var report = localSync.GetAll()
                    .Where(x => x.DevelopmentRunId == run.Id)
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefault(x =>
                        x.Synced &&
                        !x.DirtyTreeDetected &&
                        x.State == DevelopmentLocalSyncStates.Synced);

                if (report is null)
                    return null;

                evidence.Add(new(
                    run.Stage,
                    "development-local-sync-report",
                    report.Id,
                    true,
                    "Persisted local-sync report is complete; checkout/pull are not replayed."));

                return new(
                    run.Stage,
                    "succeeded",
                    $"recovery:local-sync-report:{report.Id:D}",
                    LocalSyncReportId: report.Id);
            }

            default:
                return null;
        }
    }

    private bool CanReconcileCurrentStage(DevelopmentRun run)
    {
        if (run.Status != "active")
            return false;

        var evidence = new List<DevelopmentRecoveryEvidence>();
        return BuildAdvanceRequest(run, evidence) is not null;
    }

    private CiRunRecord? ResolveSuccessfulCi(DevelopmentRun run)
    {
        if (run.CiRunId is not null)
        {
            var linked = ci.Get(run.CiRunId.Value);
            if (IsSuccessfulCi(linked))
                return linked;
        }

        return ci.GetAll()
            .Where(x =>
                string.Equals(
                    x.Ref,
                    run.Branch,
                    StringComparison.Ordinal) &&
                IsSuccessfulCi(x))
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefault();
    }

    private static bool IsSuccessfulCi(CiRunRecord? run) =>
        run is not null &&
        run.State == CiMonitorStates.Completed &&
        string.Equals(
            run.Conclusion,
            "success",
            StringComparison.OrdinalIgnoreCase);

    private static string NextActionFor(string stage) =>
        stage switch
        {
            DevelopmentRunStages.Analysis => "continue-analysis",
            DevelopmentRunStages.Coding => "continue-coding-in-bound-worktree",
            DevelopmentRunStages.Testing => "run-development-tests",
            DevelopmentRunStages.Review => "run-independent-review",
            DevelopmentRunStages.Security => "run-security-review",
            DevelopmentRunStages.Benchmark => "run-benchmark-gate",
            DevelopmentRunStages.Push => "push-and-create-pull-request",
            DevelopmentRunStages.Ci => "wait-for-ci-or-request-repair",
            DevelopmentRunStages.MergePolicy => "evaluate-merge-policy",
            DevelopmentRunStages.LocalSync => "run-safe-local-sync",
            _ => "no-action"
        };
}
