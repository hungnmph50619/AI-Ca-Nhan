namespace PersonalAI.Web.ModelLab;

public sealed record VerifySyntheticDraftRequest(
    Guid DraftId,
    double MinimumCriticAverage = 0.80,
    double MinimumSafetyAverage = 0.90,
    double MaximumSyntheticRatio = 0.50);

public sealed record VerificationLayerResult(
    string Layer,
    bool Passed,
    string Detail,
    double? Score = null);

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
    bool EvaluationGatePassed,
    bool EligibleForTraining,
    DateTimeOffset CreatedAt);

public sealed record SyntheticVerificationStatus(
    string Version,
    string WorkspaceId,
    int Reports,
    int PassedReports,
    bool CriticRequired,
    bool DatasetValidationRequired,
    bool EvaluationGateRequired,
    bool VerificationRequiredForCommit);

public sealed class SyntheticVerificationException(string message)
    : Exception(message);
