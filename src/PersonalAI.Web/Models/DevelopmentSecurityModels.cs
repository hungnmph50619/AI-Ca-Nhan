namespace PersonalAI.Web.Models;

public static class DevelopmentSecurityCategories
{
    public const string SecretLeakage = "secret-leakage";
    public const string PermissionBypass = "permission-bypass";
    public const string Injection = "injection";
    public const string PathTraversal = "path-traversal";
    public const string DangerousExecution = "dangerous-execution";

    public static readonly IReadOnlyList<string> Required =
    [
        SecretLeakage,
        PermissionBypass,
        Injection,
        PathTraversal,
        DangerousExecution
    ];
}

public sealed record RunDevelopmentSecurityRequest(
    Guid DevelopmentRunId,
    Guid ReviewReportId,
    bool ConfirmSecurityReview = false);

public sealed record DevelopmentSecurityFinding(
    string Category,
    string RuleId,
    string Severity,
    string Title,
    string Detail);

public sealed record DevelopmentSecurityReport(
    Guid Id,
    string WorkspaceId,
    Guid DevelopmentRunId,
    Guid ReviewReportId,
    string RepositoryPath,
    string Branch,
    string WorktreePath,
    IReadOnlyList<DevelopmentSecurityFinding> Findings,
    IReadOnlyList<string> CheckedCategories,
    bool HighRiskFound,
    bool PromotionAllowed,
    DateTimeOffset CreatedAt);

public sealed record DevelopmentSecurityStatus(
    string Version,
    string WorkspaceId,
    int Reports,
    int PromotionAllowedReports,
    int BlockedReports,
    bool ReportsPersisted,
    bool HighRiskBlocksPromotion,
    bool WorktreeOnly,
    IReadOnlyList<string> RequiredCategories);

public sealed class DevelopmentSecurityValidationException(string message)
    : Exception(message);
