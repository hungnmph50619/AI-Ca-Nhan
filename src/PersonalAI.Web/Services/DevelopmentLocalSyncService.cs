using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentLocalSyncService
{
    DevelopmentLocalSyncStatus GetStatus();
    IReadOnlyList<DevelopmentLocalSyncReport> GetAll();
    DevelopmentLocalSyncReport? Get(Guid id);
    Task<DevelopmentLocalSyncReport> RunAsync(
        RunDevelopmentLocalSyncRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DevelopmentLocalSyncService(
    IDevelopmentRunService runs,
    IDevelopmentMergePolicyReportStore mergePolicyReports,
    ILocalGitRepositoryService git,
    IDevelopmentLocalSyncReportStore store,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IDevelopmentLocalSyncService
{
    public DevelopmentLocalSyncStatus GetStatus()
    {
        var all = store.GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.Synced),
            all.Count(x => x.State == DevelopmentLocalSyncStates.DeferredDirtyTree),
            DirtyTreeDefersSync: true,
            PullFastForwardOnly: true,
            ResetEnabled: false,
            CleanEnabled: false);
    }

    public IReadOnlyList<DevelopmentLocalSyncReport> GetAll() =>
        store.GetAll();

    public DevelopmentLocalSyncReport? Get(Guid id) =>
        store.Get(id);

    public async Task<DevelopmentLocalSyncReport> RunAsync(
        RunDevelopmentLocalSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmSync)
            throw new DevelopmentLocalSyncValidationException(
                "Cần ConfirmSync=true để đồng bộ code về main/master.");

        var run = runs.Get(request.DevelopmentRunId)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

        if (run.WorkspaceId != workspace.CurrentWorkspaceId)
            throw new DevelopmentLocalSyncValidationException(
                "DevelopmentRun không thuộc workspace hiện tại.");

        if (run.Status != "active" ||
            run.Stage != DevelopmentRunStages.LocalSync)
        {
            throw new DevelopmentLocalSyncValidationException(
                "Local sync chỉ chạy khi DevelopmentRun đang ở stage local-sync.");
        }

        if (run.MergePolicyReportId is null)
            throw new DevelopmentLocalSyncValidationException(
                "DevelopmentRun chưa có MergePolicyReportId.");

        var merge = mergePolicyReports.Get(run.MergePolicyReportId.Value)
            ?? throw new KeyNotFoundException(
                "Không tìm thấy DevelopmentMergePolicyReport.");

        if (merge.DevelopmentRunId != run.Id || !merge.Merged)
            throw new DevelopmentLocalSyncValidationException(
                "Pull Request chưa được merge thành công.");

        var baseBranch = NormalizeBaseBranch(request.BaseBranch);
        var remote = NormalizeRemote(request.Remote);

        var branchesBefore = await git.BranchesAsync(
            run.RepositoryPath,
            cancellationToken);
        var branchBefore = branchesBefore.CurrentBranch;

        var fetch = await git.FetchAsync(
            new LocalGitFetchRequest(
                run.RepositoryPath,
                remote,
                ConfirmGitWrite: true,
                request.CredentialRef),
            cancellationToken);

        if (!fetch.Succeeded)
        {
            return SaveReport(
                new DevelopmentLocalSyncReport(
                    Guid.NewGuid(),
                    workspace.CurrentWorkspaceId,
                    run.Id,
                    run.RepositoryPath,
                    remote,
                    baseBranch,
                    branchBefore,
                    branchBefore,
                    DevelopmentLocalSyncStates.Failed,
                    FetchSucceeded: false,
                    DirtyTreeDetected: false,
                    CheckoutPerformed: false,
                    PullFastForwardOnly: true,
                    Synced: false,
                    DeferredReason: "fetch-failed",
                    DateTimeOffset.UtcNow));
        }

        var status = await git.StatusAsync(
            run.RepositoryPath,
            cancellationToken);

        if (!status.Succeeded)
            throw new DevelopmentLocalSyncValidationException(
                "Không đọc được Git status sau fetch.");

        if (HasDirtyWorktree(status.Output))
        {
            return SaveReport(
                new DevelopmentLocalSyncReport(
                    Guid.NewGuid(),
                    workspace.CurrentWorkspaceId,
                    run.Id,
                    run.RepositoryPath,
                    remote,
                    baseBranch,
                    branchBefore,
                    branchBefore,
                    DevelopmentLocalSyncStates.DeferredDirtyTree,
                    FetchSucceeded: true,
                    DirtyTreeDetected: true,
                    CheckoutPerformed: false,
                    PullFastForwardOnly: true,
                    Synced: false,
                    DeferredReason: "working-tree-dirty",
                    DateTimeOffset.UtcNow));
        }

        var checkoutPerformed = false;
        if (!branchBefore.Equals(
                baseBranch,
                StringComparison.Ordinal))
        {
            var checkout = await git.CheckoutAsync(
                new LocalGitCheckoutRequest(
                    run.RepositoryPath,
                    baseBranch,
                    ConfirmGitWrite: true),
                cancellationToken);

            if (!checkout.Succeeded)
            {
                return SaveReport(
                    new DevelopmentLocalSyncReport(
                        Guid.NewGuid(),
                        workspace.CurrentWorkspaceId,
                        run.Id,
                        run.RepositoryPath,
                        remote,
                        baseBranch,
                        branchBefore,
                        branchBefore,
                        DevelopmentLocalSyncStates.Failed,
                        FetchSucceeded: true,
                        DirtyTreeDetected: false,
                        CheckoutPerformed: false,
                        PullFastForwardOnly: true,
                        Synced: false,
                        DeferredReason: "checkout-failed",
                        DateTimeOffset.UtcNow));
            }

            checkoutPerformed = true;
        }

        var pull = await git.PullAsync(
            new LocalGitPullRequest(
                run.RepositoryPath,
                remote,
                baseBranch,
                ConfirmGitWrite: true,
                request.CredentialRef),
            cancellationToken);

        var branchesAfter = await git.BranchesAsync(
            run.RepositoryPath,
            cancellationToken);

        var report = new DevelopmentLocalSyncReport(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            run.Id,
            run.RepositoryPath,
            remote,
            baseBranch,
            branchBefore,
            branchesAfter.CurrentBranch,
            pull.Succeeded
                ? DevelopmentLocalSyncStates.Synced
                : DevelopmentLocalSyncStates.Failed,
            FetchSucceeded: true,
            DirtyTreeDetected: false,
            CheckoutPerformed: checkoutPerformed,
            PullFastForwardOnly: true,
            Synced: pull.Succeeded &&
                    branchesAfter.CurrentBranch.Equals(
                        baseBranch,
                        StringComparison.Ordinal),
            DeferredReason: pull.Succeeded ? null : "pull-ff-only-failed",
            DateTimeOffset.UtcNow);

        return SaveReport(report);
    }

    private DevelopmentLocalSyncReport SaveReport(
        DevelopmentLocalSyncReport report)
    {
        store.Save(report);

        audit.Record(
            AuditAgents.System,
            "development.local-sync",
            $"development-run:{report.DevelopmentRunId:D}",
            $"report:{report.Id:D};state:{report.State};dirty:{report.DirtyTreeDetected};fetch:{report.FetchSucceeded};checkout:{report.CheckoutPerformed};ff-only:{report.PullFastForwardOnly};synced:{report.Synced}",
            report.Synced
                ? AuditResults.Succeeded
                : report.State == DevelopmentLocalSyncStates.DeferredDirtyTree
                    ? AuditResults.Prepared
                    : AuditResults.Failed);

        return report;
    }

    private static bool HasDirtyWorktree(string? output)
    {
        foreach (var raw in (output ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
                continue;

            if (!string.IsNullOrWhiteSpace(line))
                return true;
        }

        return false;
    }

    private static string NormalizeBaseBranch(string? value)
    {
        var branch = (value ?? string.Empty).Trim();
        if (branch is not ("main" or "master"))
            throw new DevelopmentLocalSyncValidationException(
                "BaseBranch phải là main hoặc master.");
        return branch;
    }

    private static string NormalizeRemote(string? value)
    {
        var remote = (value ?? string.Empty).Trim();
        if (remote.Length is < 1 or > 80 ||
            remote.StartsWith('-') ||
            remote.Any(c =>
                !(char.IsLetterOrDigit(c) ||
                  c is '-' or '_' or '.')))
        {
            throw new DevelopmentLocalSyncValidationException(
                "Remote không hợp lệ.");
        }

        return remote;
    }
}
