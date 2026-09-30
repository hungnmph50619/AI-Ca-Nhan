namespace PersonalAI.Web.SelfImprovement;

public sealed record PrepareAutomatedExperimentRequest(
    ImprovementProposal Proposal,
    string RepositoryPath = ".",
    string BaseBranch = "main");

public sealed record AutomatedExperimentPlan(
    string ExperimentId,
    string ProposalId,
    string WorkspaceId,
    string RepositoryPath,
    string BaseBranch,
    string ExperimentBranch,
    string Status,
    string IsolationMode,
    string Objective,
    string VerificationPlan,
    string RollbackPlan,
    bool GitAvailable,
    bool GitWriteActionsEnabled,
    bool BranchCreationPerformed,
    bool HumanApprovalRequired,
    DateTimeOffset PreparedAt);

public static class AutomatedExperimentStatuses
{
    public const string Prepared = "prepared";
    public const string BranchCreationBlocked = "branch-creation-blocked";
}

public sealed class AutomatedExperimentValidationException(string message)
    : Exception(message);
