using PersonalAI.Web.Evaluation.Benchmarks;
using PersonalAI.Web.Evaluation.Comparison;

namespace PersonalAI.Web.SelfImprovement;

public sealed record UserCorrectionSignal(
    string Area,
    string Description);

public sealed record SelfEvaluationRequest(
    IReadOnlyList<AgentBenchmarkReport>? Benchmarks = null,
    IReadOnlyList<ModelComparisonReport>? Comparisons = null,
    IReadOnlyList<UserCorrectionSignal>? Corrections = null,
    int? AuditLimit = null);

public sealed record SelfEvaluationWeakness(
    string Area,
    string Signal,
    string Severity,
    string Evidence,
    int EvidenceCount);

public sealed record SelfEvaluationReport(
    string Version,
    string WorkspaceId,
    DateTimeOffset EvaluatedAt,
    int AuditEventsRead,
    int BenchmarkReportsRead,
    int ComparisonReportsRead,
    int CorrectionsRead,
    IReadOnlyList<SelfEvaluationWeakness> Weaknesses,
    bool ReadOnly,
    bool AutomaticChangesEnabled);

public sealed class SelfEvaluationValidationException(string message)
    : Exception(message);
