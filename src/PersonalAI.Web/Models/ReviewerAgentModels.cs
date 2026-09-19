namespace PersonalAI.Web.Models;

public static class ReviewerStatuses
{
    public const string NeedsUserReview = "needs-user-review";
    public const string InsufficientMaterial = "insufficient-material";
}

public static class ReviewerFindingKinds
{
    public const string Ambiguity = "ambiguity";
    public const string Inconsistency = "inconsistency";
    public const string UnsupportedClaim = "unsupported-claim";
    public const string MissingInformation = "missing-information";
    public const string Clarity = "clarity";
    public static readonly IReadOnlyList<string> All =
        [Ambiguity, Inconsistency, UnsupportedClaim, MissingInformation, Clarity];
}

public sealed record ReviewerFinding(
    string Kind,
    string Observation,
    string EvidenceExcerpt,
    string SuggestedRevision,
    string VerificationStep);

public sealed record ReviewerReport(
    string Status,
    string Summary,
    IReadOnlyList<ReviewerFinding> Findings,
    IReadOnlyList<string> Questions,
    IReadOnlyList<string> Limitations,
    bool EvidenceExcerptsValidated,
    bool IndependentlyVerified,
    bool Approved,
    bool ModifiedContent,
    bool ExecutedTools,
    bool DispatchedAgents,
    DateTimeOffset CreatedAt);

public sealed record ReviewerAgentStatusResponse(
    string Version,
    string AgentId,
    int MinimumReviewCharacters,
    int MaximumReviewCharacters,
    int MaximumFindings,
    IReadOnlyList<string> AllowedFindingKinds,
    bool ExactEvidenceValidation,
    bool ParserSelfTestPassed,
    bool IndependentlyVerifiesClaims,
    bool ApprovesResults,
    bool ModifiesContent,
    bool ExecutesTools,
    bool DispatchesAgents,
    bool RequiresExplicitInvocation,
    string NextStage);
