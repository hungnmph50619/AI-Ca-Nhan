namespace PersonalAI.Web.Models;

public static class DeveloperReportStatuses
{
    public const string Reviewed = "reviewed";
    public const string InsufficientContext = "insufficient-context";
}

public sealed record DeveloperCodeReference(
    int EvidenceNumber,
    string Path,
    int LineNumber,
    string Preview);

public sealed record DeveloperFinding(
    string Observation,
    IReadOnlyList<int> EvidenceNumbers,
    string SuggestedChange,
    string VerificationStep);

public sealed record DeveloperReport(
    string Goal,
    string Status,
    string Summary,
    IReadOnlyList<DeveloperFinding> Findings,
    IReadOnlyList<DeveloperCodeReference> Evidence,
    IReadOnlyList<DevelopmentProjectEntry> Projects,
    IReadOnlyList<string> Limitations,
    bool ReadOnly,
    bool RanCommands,
    bool ModifiedFiles,
    bool CreatedCommits,
    bool DispatchedAgents,
    DateTimeOffset CreatedAt);

public sealed record DeveloperAgentStatusResponse(
    string Version,
    string AgentId,
    int MaximumSearchHits,
    int MaximumFindings,
    bool EvidenceIndicesValidated,
    bool ParserSelfTestPassed,
    bool ReadOnly,
    bool RunsCommands,
    bool ModifiesFiles,
    bool CreatesCommits,
    bool DispatchesAgents,
    bool RequiresExplicitInvocation,
    string NextStage);
