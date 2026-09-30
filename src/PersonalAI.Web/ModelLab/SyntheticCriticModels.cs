using System.Text.Json;

namespace PersonalAI.Web.ModelLab;

public sealed record ReviewSyntheticDraftRequest(
    Guid DraftId,
    double MinimumScore = 0.75,
    bool ConfirmExternalAi = false);

public sealed record SyntheticCriticItemReview(
    int Index,
    double Correctness,
    double Relevance,
    double Consistency,
    double Safety,
    double Overall,
    bool Accepted,
    string Reason);

public sealed record SyntheticCriticReport(
    Guid Id,
    Guid DraftId,
    string WorkspaceId,
    string Provider,
    string Model,
    bool SemanticReviewed,
    double MinimumScore,
    int Items,
    int AcceptedItems,
    int RejectedItems,
    bool Passed,
    IReadOnlyList<SyntheticCriticItemReview> Reviews,
    DateTimeOffset CreatedAt);

public sealed record SyntheticCriticStatus(
    string Version,
    string WorkspaceId,
    int Reports,
    int PassedReports,
    double DefaultMinimumScore,
    bool SemanticReviewRequiredForCommit,
    bool ExternalAiRequiresConfirmation);

public sealed class SyntheticCriticValidationException(string message)
    : Exception(message);
