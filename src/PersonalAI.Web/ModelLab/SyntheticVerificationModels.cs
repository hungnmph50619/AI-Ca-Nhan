namespace PersonalAI.Web.ModelLab;

public sealed record VerifySyntheticDraftRequest(
    Guid DraftId,
    double MinimumCriticAverage = 0.80,
    double MinimumSafetyAverage = 0.90,
    double MaximumSyntheticRatio = 0.50,
    bool HumanReviewed = false,
    string? HumanReviewDecision = null);

public sealed record VerificationLayerResult(
    string Layer,
    bool Passed,
    string Detail,
    double? Score = null);

public static class SyntheticVerificationStatuses
{
    public const string Verified = "verified";
    public const string Rejected = "rejected";
}

public sealed record SyntheticVerificationReport(
    Guid Id,
    Guid DraftId,
    string WorkspaceId,
    string DatasetId,
    int SourceVersion,
    string SourceSha256,
    Guid CriticReportId,
    IReadOnlyList<VerificationLayerResult> Layers,
    double CriticAverage,
    double SafetyAverage,
    double SyntheticRatio,
    bool DatasetValid,
    bool ReferenceVerified,
    bool EvaluationGatePassed,
    bool HumanReviewApplied,
    string Status,
    bool EligibleForTraining,
    DateTimeOffset CreatedAt);

public sealed record SyntheticVerificationStatus(
    string Version,
    string WorkspaceId,
    int Reports,
    int PassedReports,
    bool CriticRequired,
    bool DatasetValidationRequired,
    bool ReferenceVerificationRequired,
    bool EvaluationGateRequired,
    bool HumanReviewOptional,
    bool VerificationRequiredForCommit);

public sealed class SyntheticVerificationException(string message)
    : Exception(message);
