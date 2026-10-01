using System.Text;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentSecurityService
{
    DevelopmentSecurityStatus GetStatus();
    IReadOnlyList<DevelopmentSecurityReport> GetAll();
    DevelopmentSecurityReport? Get(Guid id);
    Task<DevelopmentSecurityReport> RunAsync(
        RunDevelopmentSecurityRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DevelopmentSecurityService(
    IDevelopmentRunService runs,
    IDevelopmentReviewService reviews,
    ILocalGitRepositoryService git,
    IWorkspaceFileService files,
    IAgentFrameworkService agents,
    IDevelopmentSecurityReportStore store,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IDevelopmentSecurityService
{
    public const int MaximumSecurityMaterialCharacters = 32_000;
    public const int MaximumSecurityChunkCharacters = 3_600;
    public const int MaximumUntrackedFiles = 20;
    public const int MaximumUntrackedCharactersPerFile = 8_000;

    public DevelopmentSecurityStatus GetStatus()
    {
        var all = store.GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.PromotionAllowed),
            all.Count(x => !x.PromotionAllowed),
            ReportsPersisted: true,
            HighRiskBlocksPromotion: true,
            WorktreeOnly: true,
            DevelopmentSecurityCategories.Required);
    }

    public IReadOnlyList<DevelopmentSecurityReport> GetAll() =>
        store.GetAll();

    public DevelopmentSecurityReport? Get(Guid id) =>
        store.Get(id);

    public async Task<DevelopmentSecurityReport> RunAsync(
        RunDevelopmentSecurityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmSecurityReview)
            throw new DevelopmentSecurityValidationException(
                "Cần ConfirmSecurityReview=true để chạy security review.");

        var run = runs.Get(request.DevelopmentRunId)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

        if (run.WorkspaceId != workspace.CurrentWorkspaceId)
            throw new DevelopmentSecurityValidationException(
                "DevelopmentRun không thuộc workspace hiện tại.");

        if (run.Status != "active" ||
            run.Stage != DevelopmentRunStages.Security)
        {
            throw new DevelopmentSecurityValidationException(
                "Security review chỉ chạy khi DevelopmentRun đang ở stage security.");
        }

        if (!run.Branch.StartsWith("experiment/", StringComparison.Ordinal))
            throw new DevelopmentSecurityValidationException(
                "Security review chỉ chạy trên experiment branch.");

        var review = reviews.Get(request.ReviewReportId)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentReviewReport.");

        if (review.DevelopmentRunId != run.Id ||
            !review.Branch.Equals(run.Branch, StringComparison.Ordinal) ||
            !review.Approved)
        {
            throw new DevelopmentSecurityValidationException(
                "Security review yêu cầu independent code review đã approved cho đúng DevelopmentRun.");
        }

        if (!review.WorktreePath.Contains(
                DevelopmentWorktreeService.AgentWorktreeDirectory,
                StringComparison.Ordinal))
        {
            throw new DevelopmentSecurityValidationException(
                "Security review chỉ đọc isolated agent worktree.");
        }

        var diff = await git.DiffAsync(
            new LocalGitDiffRequest(
                review.WorktreePath,
                Staged: false),
            cancellationToken);

        if (!diff.Succeeded)
            throw new DevelopmentSecurityValidationException(
                "Không đọc được worktree diff.");

        var status = await git.StatusAsync(
            review.WorktreePath,
            cancellationToken);

        if (!status.Succeeded)
            throw new DevelopmentSecurityValidationException(
                "Không đọc được worktree status.");

        var material = await BuildSecurityMaterialAsync(
            review.WorktreePath,
            diff.Output,
            status.Output,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(material))
            throw new DevelopmentSecurityValidationException(
                "Không có source change để security review.");

        var findings = new List<DevelopmentSecurityFinding>();

        foreach (var chunk in Chunk(material, MaximumSecurityChunkCharacters))
        {
            var response = await agents.ExecuteAsync(
                SecurityFrameworkAgent.AgentId,
                new AgentExecutionRequest(
                    chunk,
                    Conversation: null,
                    UseKnowledge: false,
                    UseMemory: false,
                    UseTaskContext: false,
                    UseLifeContext: false),
                cancellationToken);

            var security = response.Security
                ?? throw new DevelopmentSecurityValidationException(
                    "Security Agent không trả SecurityReviewReport.");

            foreach (var finding in security.Findings)
            {
                var mapped = new DevelopmentSecurityFinding(
                    MapCategory(finding.RuleId),
                    finding.RuleId,
                    finding.Severity,
                    finding.Title,
                    finding.Explanation);

                if (!findings.Any(x =>
                    x.RuleId.Equals(mapped.RuleId, StringComparison.Ordinal) &&
                    x.Category.Equals(mapped.Category, StringComparison.Ordinal)))
                {
                    findings.Add(mapped);
                }
            }
        }

        var highRisk = findings.Any(x =>
            x.Severity.Equals(
                SecurityFindingSeverities.High,
                StringComparison.OrdinalIgnoreCase));

        var report = new DevelopmentSecurityReport(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            run.Id,
            review.Id,
            run.RepositoryPath,
            run.Branch,
            review.WorktreePath,
            findings.ToArray(),
            DevelopmentSecurityCategories.Required,
            HighRiskFound: highRisk,
            PromotionAllowed: !highRisk,
            DateTimeOffset.UtcNow);

        store.Save(report);

        audit.Record(
            AuditAgents.System,
            "development.security.complete",
            $"development-run:{run.Id:D}",
            $"report:{report.Id:D};findings:{findings.Count};high-risk:{highRisk};promotion-allowed:{report.PromotionAllowed}",
            highRisk ? AuditResults.Failed : AuditResults.Succeeded);

        return report;
    }

    private async Task<string> BuildSecurityMaterialAsync(
        string worktreePath,
        string diff,
        string status,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("DEVELOPMENT SECURITY REVIEW v2.7.6");
        builder.AppendLine("Review source changes only. Do not echo secrets.");
        builder.AppendLine();
        builder.AppendLine("TRACKED DIFF:");
        builder.AppendLine(diff);

        var untracked = ParseUntracked(status)
            .Take(MaximumUntrackedFiles)
            .ToArray();

        foreach (var path in untracked)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var file = await files.ReadTextAsync(
                    PrefixWorktreePath(worktreePath, path),
                    MaximumUntrackedCharactersPerFile,
                    cancellationToken);

                builder.AppendLine();
                builder.Append("UNTRACKED FILE: ").AppendLine(path);
                builder.AppendLine(file.Content);
            }
            catch (Exception exception) when (
                exception is WorkspaceFileValidationException or
                IOException or
                UnauthorizedAccessException)
            {
                builder.AppendLine();
                builder.Append("UNTRACKED FILE NOT READABLE: ")
                    .AppendLine(path);
            }
        }

        var material = builder.ToString();
        return material.Length <= MaximumSecurityMaterialCharacters
            ? material
            : material[..MaximumSecurityMaterialCharacters];
    }

    private static IEnumerable<string> ParseUntracked(string status)
    {
        foreach (var raw in (status ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.TrimEnd();
            if (!line.StartsWith("?? ", StringComparison.Ordinal))
                continue;

            var path = line[3..].Trim().Replace('\\', '/');
            if (path.Length == 0 ||
                Path.IsPathRooted(path) ||
                path.StartsWith("../", StringComparison.Ordinal) ||
                path.Contains("/../", StringComparison.Ordinal) ||
                path.Split('/').Any(x =>
                    x.Equals(".git", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            yield return path;
        }
    }

    private static IEnumerable<string> Chunk(string value, int maximum)
    {
        for (var offset = 0; offset < value.Length; offset += maximum)
            yield return value.Substring(
                offset,
                Math.Min(maximum, value.Length - offset));
    }

    private static string MapCategory(string ruleId) =>
        ruleId switch
        {
            "credential-material" =>
                DevelopmentSecurityCategories.SecretLeakage,
            "permission-bypass" =>
                DevelopmentSecurityCategories.PermissionBypass,
            "command-injection" =>
                DevelopmentSecurityCategories.Injection,
            "path-traversal" =>
                DevelopmentSecurityCategories.PathTraversal,
            "destructive-action" or
            "remote-code-execution" or
            "external-transfer" =>
                DevelopmentSecurityCategories.DangerousExecution,
            _ => DevelopmentSecurityCategories.DangerousExecution
        };

    private static string PrefixWorktreePath(
        string worktreePath,
        string relativePath)
    {
        var prefix = worktreePath.Trim().Replace('\\', '/').Trim('/');
        var path = relativePath.Trim().Replace('\\', '/').Trim('/');
        if (prefix.Length == 0 || path.Length == 0)
            throw new DevelopmentSecurityValidationException(
                "Worktree/file path không hợp lệ.");
        return $"{prefix}/{path}";
    }
}
