namespace PersonalAI.Web.Models;

public static class DevelopmentReviewStatuses
{
    public const string Approved = "approved";
    public const string ChangesRequired = "changes-required";
}

public sealed record RunDevelopmentReviewRequest(
    Guid DevelopmentRunId,
    Guid TestReportId,
    string CodingAgentId,
    string ReviewerId,
    bool ConfirmReview = false);

public sealed record DevelopmentReviewCheck(
    string Category,
    string Status,
    string Detail);

public sealed record DevelopmentReviewReport(
    Guid Id,
    string WorkspaceId,
    Guid DevelopmentRunId,
    Guid TestReportId,
    string RepositoryPath,
    string Branch,
    string WorktreePath,
    string CodingAgentId,
    string ReviewerId,
    IReadOnlyList<DevelopmentReviewCheck> Checks,
    string Status,
    bool Approved,
    bool IndependentReviewer,
    DateTimeOffset CreatedAt);

public sealed record DevelopmentReviewStatus(
    string Version,
    string WorkspaceId,
    int Reports,
    int ApprovedReports,
    int ChangesRequiredReports,
    bool ReportsPersisted,
    bool IndependentReviewerRequired,
    bool DeveloperSelfApprovalAllowed,
    IReadOnlyList<string> RequiredChecks);

public sealed class DevelopmentReviewValidationException(string message)
    : Exception(message);
