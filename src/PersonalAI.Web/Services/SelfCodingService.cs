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
    IDevelopmentAgentService development,
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
                "Self Coding cần xác nhận riêng cho tạo branch và thay đổi file.");

        var experiment = request.Experiment;
        if (!string.Equals(
            experiment.WorkspaceId,
            workspace.CurrentWorkspaceId,
            StringComparison.OrdinalIgnoreCase))
            throw new SelfCodingValidationException(
                "Experiment không thuộc workspace hiện tại.");

        if (!string.Equals(
            experiment.ProposalId,
            request.Proposal.Id,
            StringComparison.Ordinal))
            throw new SelfCodingValidationException(
                "Proposal không khớp experiment.");

        if (!experiment.ExperimentBranch.StartsWith(
            "experiment/",
            StringComparison.Ordinal))
            throw new SelfCodingValidationException(
                "Self Coding chỉ được chạy trên branch experiment/*.");

        if (!request.Proposal.RequiresHumanApproval)
            throw new SelfCodingValidationException(
                "Proposal phải yêu cầu human approval.");

        if (request.Edits is null ||
            request.Edits.Count is < 1 or > MaximumEdits)
            throw new SelfCodingValidationException(
                $"Mỗi Self Coding run phải có từ 1 đến {MaximumEdits} file edit.");

        ValidateEdits(request.Edits);

        var current = await development.GetCurrentBranchAsync(
            experiment.RepositoryPath,
            cancellationToken);
        if (!current.Succeeded)
            throw new SelfCodingValidationException(
                "Không đọc được branch hiện tại.");

        var branchCreated = false;
        if (!string.Equals(
            current.Branch,
            experiment.ExperimentBranch,
            StringComparison.Ordinal))
        {
            if (!string.Equals(
                current.Branch,
                experiment.BaseBranch,
                StringComparison.Ordinal))
            {
                throw new SelfCodingValidationException(
                    $"Repository phải đang ở base branch '{experiment.BaseBranch}' hoặc đúng experiment branch.");
            }

            var created = await development.CreateExperimentBranchAsync(
                experiment.RepositoryPath,
                experiment.BaseBranch,
                experiment.ExperimentBranch,
                confirmed: true,
                cancellationToken);
            if (!created.Succeeded)
                throw new SelfCodingValidationException(
                    "Không tạo được experiment branch.");
            branchCreated = true;
        }

        var verifyBranch = await development.GetCurrentBranchAsync(
            experiment.RepositoryPath,
            cancellationToken);
        if (!verifyBranch.Succeeded ||
            !string.Equals(
                verifyBranch.Branch,
                experiment.ExperimentBranch,
                StringComparison.Ordinal))
        {
            throw new SelfCodingValidationException(
                "Không xác minh được experiment branch trước khi sửa file.");
        }

        var files = new List<SelfCodingFileResult>();
        foreach (var edit in request.Edits)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await workspaceFiles.WriteTextAsync(
                edit.Path,
                edit.Content,
                edit.Mode,
                edit.ExpectedSha256,
                cancellationToken);

            files.Add(new SelfCodingFileResult(
                result.Path,
                result.Mode,
                result.Created,
                result.Sha256));

            audit.Record(
                AuditAgents.System,
                "self-improvement.self-coding.write",
                $"file:{result.Path}",
                $"experiment:{experiment.ExperimentId}",
                AuditResults.Succeeded);
        }

        audit.Record(
            AuditAgents.System,
            "self-improvement.self-coding.complete",
            $"experiment:{experiment.ExperimentId}",
            "confirmed-experiment-file-changes",
            AuditResults.Succeeded);

        return new SelfCodingResult(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            experiment.ExperimentId,
            experiment.ExperimentBranch,
            branchCreated,
            files.Count,
            files,
            CommitCreated: false,
            Pushed: false,
            Merged: false,
            CompletedAt: DateTimeOffset.UtcNow);
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
                path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Any(segment => segment == "..") ||
                path.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                !paths.Add(path))
            {
                throw new SelfCodingValidationException(
                    "File edit có path không hợp lệ, trùng hoặc chạm .git.");
            }

            if ((edit.Content ?? string.Empty).Length > MaximumContentCharacters)
                throw new SelfCodingValidationException(
                    $"Nội dung mỗi edit tối đa {MaximumContentCharacters:N0} ký tự.");

            var mode = (edit.Mode ?? string.Empty).Trim().ToLowerInvariant();
            if (mode is not ("create" or "overwrite"))
                throw new SelfCodingValidationException(
                    "Self Coding chỉ cho phép create hoặc overwrite; không append.");

            if (mode == "overwrite" &&
                string.IsNullOrWhiteSpace(edit.ExpectedSha256))
                throw new SelfCodingValidationException(
                    "Overwrite bắt buộc có expectedSha256 để tránh ghi đè phiên bản mới.");
        }
    }
}
