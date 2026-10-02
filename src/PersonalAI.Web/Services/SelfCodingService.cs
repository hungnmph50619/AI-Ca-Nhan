using PersonalAI.Web.Models;
using PersonalAI.Web.SelfImprovement;

namespace PersonalAI.Web.Services;

public interface ISelfCodingService
{
    Task<SelfCodingResult> RunAsync(
        RunSelfCodingRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SelfCodingService(
    IDevelopmentRunService runs,
    IDevelopmentWorktreeService worktrees,
    IDevelopmentRunWorktreeService runWorktrees,
    IDevelopmentLeaseService leases,
    IWorkspaceFileService workspaceFiles,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : ISelfCodingService
{
    public const int MaximumEdits = 8;
    public const int MaximumContentCharacters = 200_000;

    public async Task<SelfCodingResult> RunAsync(
        RunSelfCodingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Experiment);
        ArgumentNullException.ThrowIfNull(request.Proposal);

        if (!request.ConfirmBranchCreation || !request.ConfirmFileChanges)
            throw new SelfCodingValidationException(
                "Self Coding cần xác nhận riêng cho worktree/branch và thay đổi file.");

        if (request.DevelopmentRunId is null)
            throw new SelfCodingValidationException(
                "v2.7.3 yêu cầu DevelopmentRunId để kiểm soát coding stage.");

        var experiment = request.Experiment;
        if (!string.Equals(
            experiment.WorkspaceId,
            workspace.CurrentWorkspaceId,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new SelfCodingValidationException(
                "Experiment không thuộc workspace hiện tại.");
        }

        if (!string.Equals(
            experiment.ProposalId,
            request.Proposal.Id,
            StringComparison.Ordinal))
        {
            throw new SelfCodingValidationException(
                "Proposal không khớp experiment.");
        }

        if (!experiment.ExperimentBranch.StartsWith(
                "experiment/",
                StringComparison.Ordinal) ||
            experiment.ExperimentBranch.Equals(
                "main",
                StringComparison.OrdinalIgnoreCase) ||
            experiment.ExperimentBranch.Equals(
                "master",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SelfCodingValidationException(
                "Self Coding chỉ được chạy trên branch experiment/*.");
        }

        if (!request.Proposal.RequiresHumanApproval)
            throw new SelfCodingValidationException(
                "Proposal phải yêu cầu human approval.");

        if (request.Edits is null ||
            request.Edits.Count is < 1 or > MaximumEdits)
        {
            throw new SelfCodingValidationException(
                $"Mỗi Self Coding run phải có từ 1 đến {MaximumEdits} file edit.");
        }

        ValidateEdits(request.Edits);

        var run = runs.Get(request.DevelopmentRunId.Value)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

        if (!run.WorkspaceId.Equals(
                workspace.CurrentWorkspaceId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SelfCodingValidationException(
                "DevelopmentRun không thuộc workspace hiện tại.");
        }

        if (run.Status != "active" ||
            run.Stage != DevelopmentRunStages.Coding)
        {
            throw new SelfCodingValidationException(
                "Self Coding chỉ được phép khi DevelopmentRun đang ở stage coding.");
        }

        if (!run.Branch.Equals(
                experiment.ExperimentBranch,
                StringComparison.Ordinal))
        {
            throw new SelfCodingValidationException(
                "DevelopmentRun branch không khớp experiment branch.");
        }

        if (!SameRepositoryPath(
                run.RepositoryPath,
                experiment.RepositoryPath))
        {
            throw new SelfCodingValidationException(
                "DevelopmentRun repository không khớp experiment repository.");
        }

        var owner = $"self-coding:{run.Id:D}";
        var branchLease = leases.Acquire(
            new AcquireDevelopmentLeaseRequest(
                owner,
                DevelopmentLeaseResourceTypes.Branch,
                experiment.RepositoryPath,
                experiment.ExperimentBranch,
                null,
                DevelopmentLeaseService.MaximumLeaseSeconds));

        try
        {
            var allWorktrees = await worktrees.GetAllAsync(
                experiment.RepositoryPath,
                cancellationToken);

            var mainOnExperiment = allWorktrees.FirstOrDefault(x =>
                x.IsMainWorktree &&
                x.Branch.Equals(
                    experiment.ExperimentBranch,
                    StringComparison.Ordinal));

            if (mainOnExperiment is not null)
            {
                throw new SelfCodingValidationException(
                    "Experiment branch đang được checkout ở main worktree; từ chối sửa trực tiếp working tree chính.");
            }

            var existingBinding = runWorktrees.GetByRun(run.Id);
            var binding = await runWorktrees.EnsureAsync(
                new EnsureDevelopmentRunWorktreeRequest(
                    run.Id,
                    experiment.BaseBranch,
                    ConfirmCreateWorktree: true),
                cancellationToken);

            var worktreeCreated = existingBinding is null;
            var agentWorktree = (await worktrees.GetAllAsync(
                    experiment.RepositoryPath,
                    cancellationToken))
                .FirstOrDefault(x =>
                    !x.IsMainWorktree &&
                    x.WorktreePath.Equals(
                        binding.WorktreePath,
                        StringComparison.OrdinalIgnoreCase) &&
                    x.Branch.Equals(
                        experiment.ExperimentBranch,
                        StringComparison.Ordinal));

            if (agentWorktree is null ||
                string.IsNullOrWhiteSpace(agentWorktree.WorktreePath))
            {
                throw new SelfCodingValidationException(
                    "Không xác minh được worktree riêng của DevelopmentRun.");
            }

            var files = new List<SelfCodingFileResult>();

            foreach (var edit in request.Edits)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var targetPath = PrefixWorktreePath(
                    agentWorktree.WorktreePath,
                    edit.Path);

                var result = await workspaceFiles.WriteTextAsync(
                    targetPath,
                    edit.Content,
                    edit.Mode,
                    edit.ExpectedSha256,
                    cancellationToken);

                files.Add(new SelfCodingFileResult(
                    edit.Path.Replace('\\', '/'),
                    result.Mode,
                    result.Created,
                    result.Sha256));

                audit.Record(
                    AuditAgents.System,
                    "self-improvement.self-coding.write",
                    $"file:{edit.Path.Replace('\\', '/')}",
                    $"development-run:{run.Id:D};branch:{run.Branch};worktree:{agentWorktree.WorktreePath};sha256:{result.Sha256}",
                    AuditResults.Succeeded);
            }

            audit.Record(
                AuditAgents.System,
                "self-improvement.self-coding.complete",
                $"development-run:{run.Id:D}",
                $"experiment:{experiment.ExperimentId};worktree:{agentWorktree.WorktreePath};files:{files.Count};main-modified:false",
                AuditResults.Succeeded);

            return new SelfCodingResult(
                PersonalAiRelease.Version,
                workspace.CurrentWorkspaceId,
                experiment.ExperimentId,
                experiment.ExperimentBranch,
                BranchCreated: worktreeCreated,
                files.Count,
                files,
                CommitCreated: false,
                Pushed: false,
                Merged: false,
                CompletedAt: DateTimeOffset.UtcNow,
                DevelopmentRunId: run.Id,
                WorktreePath: agentWorktree.WorktreePath,
                MainWorktreeModified: false,
                AuditPerEdit: true);
        }
        finally
        {
            try
            {
                leases.Release(new ReleaseDevelopmentLeaseRequest(
                    branchLease.Id,
                    owner));
            }
            catch
            {
                // Lease has expiry. Never alter source state because release failed.
            }
        }
    }

    private static void ValidateEdits(IReadOnlyList<SelfCodingFileEdit> edits)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var edit in edits)
        {
            if (edit is null)
                throw new SelfCodingValidationException(
                    "File edit không được null.");

            var path = (edit.Path ?? string.Empty).Trim().Replace('\\', '/');
            if (path.Length is < 1 or > 500 ||
                Path.IsPathRooted(path) ||
                path.StartsWith("../", StringComparison.Ordinal) ||
                path.Contains("/../", StringComparison.Ordinal) ||
                path.Equals("..", StringComparison.Ordinal) ||
                path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Any(segment =>
                        segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                        segment.Equals(
                            DevelopmentWorktreeService.AgentWorktreeDirectory,
                            StringComparison.OrdinalIgnoreCase)) ||
                !paths.Add(path))
            {
                throw new SelfCodingValidationException(
                    "File edit có path không hợp lệ, trùng hoặc chạm vùng Git/worktree nội bộ.");
            }

            if ((edit.Content ?? string.Empty).Length > MaximumContentCharacters)
            {
                throw new SelfCodingValidationException(
                    $"Nội dung mỗi edit tối đa {MaximumContentCharacters:N0} ký tự.");
            }

            var mode = (edit.Mode ?? string.Empty).Trim().ToLowerInvariant();
            if (mode is not ("create" or "overwrite"))
            {
                throw new SelfCodingValidationException(
                    "Self Coding chỉ cho phép create hoặc overwrite; không append.");
            }

            if (mode == "overwrite" &&
                string.IsNullOrWhiteSpace(edit.ExpectedSha256))
            {
                throw new SelfCodingValidationException(
                    "Overwrite bắt buộc có expectedSha256 để tránh ghi đè phiên bản mới.");
            }
        }
    }

    private static string PrefixWorktreePath(
        string worktreePath,
        string relativePath)
    {
        var prefix = worktreePath.Trim().Replace('\\', '/').Trim('/');
        var path = relativePath.Trim().Replace('\\', '/').Trim('/');

        if (prefix.Length == 0 || path.Length == 0)
            throw new SelfCodingValidationException(
                "Worktree/path không hợp lệ.");

        return $"{prefix}/{path}";
    }

    private static bool SameRepositoryPath(string left, string right)
    {
        static string Normalize(string value)
        {
            var normalized = (value ?? string.Empty)
                .Trim()
                .Replace('\\', '/')
                .Trim('/');

            return normalized is "." or ""
                ? string.Empty
                : normalized;
        }

        return Normalize(left).Equals(
            Normalize(right),
            StringComparison.OrdinalIgnoreCase);
    }
}
