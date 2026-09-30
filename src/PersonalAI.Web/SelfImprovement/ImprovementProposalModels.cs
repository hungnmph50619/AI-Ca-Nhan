namespace PersonalAI.Web.SelfImprovement;

public sealed record ImprovementProposalRequest(
    SelfEvaluationReport Evaluation,
    int? MaximumProposals = null);

public sealed record ImprovementProposal(
    string Id,
    string Area,
    string Severity,
    string Problem,
    string Hypothesis,
    string Change,
    string ExpectedImprovement,
    string Risk,
    string Evidence,
    int EvidenceCount,
    bool RequiresHumanApproval);

public sealed record ImprovementProposalReport(
    string Version,
    string WorkspaceId,
    DateTimeOffset CreatedAt,
    int WeaknessesRead,
    int ProposalsCreated,
    IReadOnlyList<ImprovementProposal> Proposals,
    bool ReadOnly,
    bool AutomaticChangesEnabled);

public sealed class ImprovementProposalValidationException(string message)
    : Exception(message);
