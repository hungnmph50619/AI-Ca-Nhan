namespace PersonalAI.Web.Models;

public static class DevelopmentMergeRiskLevels
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
}

public sealed record RunDevelopmentMergePolicyRequest(
    Guid DevelopmentRunId,
    Guid GitHubReportId,
    Guid ReviewReportId,
    Guid SecurityReportId,
    Guid BenchmarkReportId,
    Guid CiRunId,
    string RiskLevel,
    string RiskEvidence,
    string CredentialRef,
    bool AllowAutomaticMerge = false,
    bool ConfirmNonLowRiskMerge = false);

public sealed record DevelopmentMergePolicyCheck(
    string Check,
    bool Passed,
    string Detail);

public sealed record DevelopmentMergePolicyReport(
    Guid Id,
    string WorkspaceId,
    Guid DevelopmentRunId,
    Guid GitHubReportId,
    string RiskLevel,
    string RiskEvidence,
    IReadOnlyList<DevelopmentMergePolicyCheck> Checks,
    bool AllRequiredChecksPassed,
    bool AutomaticMergeEligible,
    bool RequiresUserConfirmation,
    bool MergeAttempted,
    bool Merged,
    string Decision,
    string? MergeCommitSha,
    DateTimeOffset CreatedAt);

public sealed record DevelopmentMergePolicyStatus(
    string Version,
    string WorkspaceId,
    int Reports,
    int MergedReports,
    bool LowRiskAutoMergeEnabled,
    bool HighRiskAutoMergeAllowed,
    bool HighRiskRequiresUserConfirmation,
    IReadOnlyList<string> RequiredChecks);

public sealed class DevelopmentMergePolicyValidationException(string message)
    : Exception(message);
