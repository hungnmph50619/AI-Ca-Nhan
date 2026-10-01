namespace PersonalAI.Web.ModelLab;

public static class PromotionGateDecisions
{
    public const string ApprovedForStaging = "approved-for-staging";
    public const string Blocked = "blocked";
}

public sealed record EvaluatePromotionGateRequest(
    Guid ComparisonReportId,
    bool CriticalRegression,
    bool SecurityPassed,
    bool LineagePassed,
    double MaximumLatencyIncreaseMilliseconds = 0,
    double MaximumCostIncreasePerCase = 0,
    double MinimumRegressionPassRate = 0.99,
    double MaximumResourceIncreaseRatio = 0.20,
    double CandidateResourceRatio = 1.0);

public sealed record PromotionGateCheck(
    string Check,
    bool Passed,
    bool Critical,
    string Detail);

public sealed record PromotionGateDecision(
    Guid Id,
    string WorkspaceId,
    Guid ComparisonReportId,
    Guid CandidateModelVersionId,
    Guid ProductionModelVersionId,
    string Family,
    IReadOnlyList<PromotionGateCheck> Checks,
    string Decision,
    bool EligibleForStaging,
    bool ProductionMutationPerformed,
    DateTimeOffset CreatedAt);

public sealed record PromotionGateStatus(
    string Version,
    string WorkspaceId,
    int Decisions,
    int Approved,
    int Blocked,
    bool CriticalRegressionAlwaysBlocks,
    bool AutoPromotionEnabled,
    bool AuditRequired);

public sealed class PromotionGateValidationException(string message)
    : Exception(message);
