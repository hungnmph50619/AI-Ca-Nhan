namespace PersonalAI.Web.Models;

public static class ResearchReportStatuses
{
    public const string Grounded = "grounded";
    public const string InsufficientEvidence = "insufficient-evidence";
}

public static class ResearchFindingStatuses
{
    public const string Sourced = "sourced";
    public const string NeedsVerification = "needs-verification";
}

public sealed record ResearchEvidence(
    int SourceNumber,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    int? PageNumber,
    string? Heading,
    string? Section);

public sealed record ResearchFinding(
    string Statement,
    string EvidenceStatus,
    IReadOnlyList<int> SourceNumbers,
    string Limitation);

public sealed record ResearchReport(
    string Query,
    string Status,
    string Summary,
    IReadOnlyList<ResearchFinding> Findings,
    IReadOnlyList<ResearchEvidence> Evidence,
    IReadOnlyList<string> UnansweredQuestions,
    IReadOnlyList<string> Limitations,
    bool ExternalWebSearchPerformed,
    bool CreatesTasks,
    bool DispatchesAgents,
    DateTimeOffset CreatedAt);

public sealed record ResearchAgentStatusResponse(
    string Version,
    string AgentId,
    string EvidenceScope,
    int MaximumSources,
    int MaximumFindings,
    bool CitationIndicesValidated,
    bool ParserSelfTestPassed,
    bool ExternalWebSearchEnabled,
    bool ToolExecutionEnabled,
    bool CreatesTasks,
    bool DispatchesAgents,
    bool RequiresExplicitInvocation,
    string NextStage);
