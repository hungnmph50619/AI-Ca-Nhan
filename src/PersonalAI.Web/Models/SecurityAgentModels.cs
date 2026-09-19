namespace PersonalAI.Web.Models;

public static class SecurityReviewStatuses
{
    public const string PatternsDetected = "patterns-detected";
    public const string NoPatternDetected = "no-pattern-detected";
}

public static class SecurityFindingSeverities
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Advisory = "advisory";
}

public sealed record SecurityReviewFinding(
    string RuleId,
    string Severity,
    string Title,
    string Explanation,
    string SuggestedCheck);

public sealed record SecurityReviewReport(
    string Status,
    string Summary,
    IReadOnlyList<SecurityReviewFinding> Findings,
    IReadOnlyList<string> Limitations,
    bool LocalOnly,
    bool InputEchoed,
    bool IndependentlyAudited,
    bool SafetyCertified,
    bool EnforcesPolicy,
    bool ChangedPermissions,
    bool ExecutedTools,
    bool DispatchedAgents,
    DateTimeOffset CreatedAt);

public sealed record SecurityAgentStatusResponse(
    string Version,
    string AgentId,
    int MaximumInputCharacters,
    int MaximumFindings,
    bool DeterministicPatternChecks,
    bool SelfTestPassed,
    bool LocalOnly,
    bool EchoesInput,
    bool IndependentlyAuditsSystems,
    bool CertifiesSafety,
    bool EnforcesPolicy,
    bool ChangesPermissions,
    bool ExecutesTools,
    bool DispatchesAgents,
    bool RequiresExplicitInvocation,
    string NextStage);
