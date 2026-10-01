namespace PersonalAI.Web.Models;

public static class RootCauseDiagnosisStages
{
    public const string EvidenceCollected = "evidence-collected";
    public const string Reproduced = "reproduced";
    public const string HypothesisProposed = "hypothesis-proposed";
    public const string Diagnosed = "diagnosed";
}

public sealed record StartRootCauseDiagnosisRequest(
    Guid ImprovementItemId,
    string RepositoryPath,
    bool ConfirmStart = false);

public sealed record RecordReproductionRequest(
    string Steps,
    string ObservedResult,
    bool Reproduced,
    string EvidenceReference);

public sealed record ProposeRootCauseHypothesisRequest(
    string Hypothesis,
    IReadOnlyList<string> EvidenceReferences);

public sealed record VerifyRootCauseHypothesisRequest(
    bool Verified,
    string VerificationMethod,
    string Result,
    string EvidenceReference);

public sealed record RootCauseHypothesis(
    Guid Id,
    string Statement,
    IReadOnlyList<string> EvidenceReferences,
    bool? Verified,
    string? VerificationMethod,
    string? VerificationResult,
    DateTimeOffset CreatedAt,
    DateTimeOffset? VerifiedAt);

public sealed record RootCauseDiagnosis(
    Guid Id,
    string WorkspaceId,
    Guid ImprovementItemId,
    string RepositoryPath,
    string Stage,
    IReadOnlyList<ImprovementEvidence> SourceEvidence,
    string? ReproductionSteps,
    string? ReproductionResult,
    string? ReproductionEvidenceReference,
    IReadOnlyList<RootCauseHypothesis> Hypotheses,
    Guid? VerifiedHypothesisId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DiagnosedAt);

public sealed record RootCauseDiagnosisStatus(
    string Version,
    string WorkspaceId,
    int Diagnoses,
    int Diagnosed,
    int Pending,
    bool SourceEditAllowedBeforeDiagnosis,
    bool EvidenceRequired,
    bool ReproductionRequired,
    bool HypothesisVerificationRequired,
    bool Persisted);

public sealed class RootCauseDiagnosisValidationException(string message)
    : Exception(message);

public sealed class RootCauseDiagnosisConflictException(string message)
    : Exception(message);
