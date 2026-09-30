using System.Security.Cryptography;
using System.Text;
using PersonalAI.Web.Models;
using PersonalAI.Web.SelfImprovement;

namespace PersonalAI.Web.Services;

public interface IAutomatedExperimentService
{
    AutomatedExperimentPlan Prepare(PrepareAutomatedExperimentRequest request);
}

public sealed class AutomatedExperimentService(
    IWorkspaceContextAccessor workspace,
    IDevelopmentAgentService development,
    IAuditRecorder audit) : IAutomatedExperimentService
{
    public AutomatedExperimentPlan Prepare(
        PrepareAutomatedExperimentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Proposal);

        var proposal = NormalizeProposal(request.Proposal);
        var repositoryPath = NormalizeRepositoryPath(request.RepositoryPath);
        var baseBranch = NormalizeBranch(request.BaseBranch, "base branch");
        var experimentId = CreateExperimentId(proposal);
        var experimentBranch =
            $"experiment/{Slug(proposal.Area)}-{experimentId["exp-".Length..]}";

        var developmentStatus = development.GetStatus();
        var branchCreationPerformed = false;
        var status = developmentStatus.GitWriteActionsEnabled
            ? AutomatedExperimentStatuses.Prepared
            : AutomatedExperimentStatuses.BranchCreationBlocked;

        audit.Record(
            AuditAgents.System,
            "self-improvement.experiment.prepare",
            $"experiment:{experimentId}",
            developmentStatus.GitWriteActionsEnabled
                ? "isolated-experiment-prepared"
                : "git-write-actions-disabled",
            status == AutomatedExperimentStatuses.Prepared
                ? AuditResults.Prepared
                : AuditResults.Blocked);

        return new AutomatedExperimentPlan(
            experimentId,
            proposal.Id,
            workspace.CurrentWorkspaceId,
            repositoryPath,
            baseBranch,
            experimentBranch,
            status,
            IsolationMode: "dedicated-git-branch",
            Objective:
                $"{proposal.Change} Expected: {proposal.ExpectedImprovement}",
            VerificationPlan:
                "Apply only the approved proposal on the experiment branch, run build/tests and the relevant regression benchmark, then compare against the baseline before any merge.",
            RollbackPlan:
                "Discard the experiment branch or reset the isolated experiment; do not change main automatically.",
            GitAvailable: developmentStatus.GitAvailable,
            GitWriteActionsEnabled: developmentStatus.GitWriteActionsEnabled,
            BranchCreationPerformed: branchCreationPerformed,
            HumanApprovalRequired: true,
            PreparedAt: DateTimeOffset.UtcNow);
    }

    private static ImprovementProposal NormalizeProposal(
        ImprovementProposal proposal)
    {
        var id = (proposal.Id ?? string.Empty).Trim();
        var area = (proposal.Area ?? string.Empty).Trim();
        var change = (proposal.Change ?? string.Empty).Trim();
        var expected = (proposal.ExpectedImprovement ?? string.Empty).Trim();
        var risk = (proposal.Risk ?? string.Empty).Trim();

        if (id.Length is < 3 or > 120 ||
            area.Length is < 2 or > 80 ||
            change.Length is < 5 or > 2000 ||
            expected.Length is < 5 or > 1000 ||
            risk.Length is < 5 or > 1000)
        {
            throw new AutomatedExperimentValidationException(
                "Improvement Proposal thiếu trường bắt buộc hoặc vượt giới hạn.");
        }

        if (!proposal.RequiresHumanApproval)
        {
            throw new AutomatedExperimentValidationException(
                "Automated Experiment chỉ chấp nhận proposal yêu cầu human approval.");
        }

        return proposal with
        {
            Id = id,
            Area = area,
            Change = change,
            ExpectedImprovement = expected,
            Risk = risk
        };
    }

    private static string NormalizeRepositoryPath(string? value)
    {
        var path = string.IsNullOrWhiteSpace(value) ? "." : value.Trim();
        if (path.Length > 500 ||
            path.IndexOf('\0') >= 0 ||
            Path.IsPathRooted(path) ||
            path.Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment == ".."))
        {
            throw new AutomatedExperimentValidationException(
                "RepositoryPath phải là đường dẫn tương đối an toàn trong workspace.");
        }

        return path.Replace('\\', '/');
    }

    private static string NormalizeBranch(string? value, string field)
    {
        var branch = (value ?? string.Empty).Trim();
        if (branch.Length is < 1 or > 120 ||
            branch.StartsWith('-') ||
            branch.Contains("..", StringComparison.Ordinal) ||
            branch.Contains(' ') ||
            branch.Any(character =>
                char.IsControl(character) ||
                character is '~' or '^' or ':' or '?' or '*' or '[' or '\\'))
        {
            throw new AutomatedExperimentValidationException(
                $"{field} không hợp lệ.");
        }
        return branch;
    }

    private static string CreateExperimentId(ImprovementProposal proposal)
    {
        var source =
            $"{proposal.Id}|{proposal.Area}|{proposal.Change}|{proposal.ExpectedImprovement}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return "exp-" + Convert.ToHexString(digest)[..12].ToLowerInvariant();
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }

            if (builder.Length >= 36)
                break;
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "change" : slug;
    }
}
