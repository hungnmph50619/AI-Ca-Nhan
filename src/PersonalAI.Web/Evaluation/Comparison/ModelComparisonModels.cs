using PersonalAI.Web.Evaluation.Benchmarks;

namespace PersonalAI.Web.Evaluation.Comparison;

public sealed record ModelComparisonCandidate(
    string Label,
    AgentBenchmarkReport Report);

public sealed record ModelComparisonRequest(
    IReadOnlyList<ModelComparisonCandidate> Candidates);

public sealed record ModelComparisonCandidateSummary(
    string Label,
    string? Provider,
    string? Model,
    int Cases,
    int PassedCases,
    int FailedCases,
    double PassRate,
    double AverageLatencyMilliseconds,
    double PassRateDeltaFromBaseline,
    double LatencyDeltaMillisecondsFromBaseline);

public sealed record ModelComparisonCaseValue(
    string Label,
    bool Passed,
    double Score,
    double LatencyMilliseconds,
    string? Provider,
    string? Model,
    IReadOnlyList<string> Failures);

public sealed record ModelComparisonCaseRow(
    string CaseId,
    string Title,
    string AgentId,
    IReadOnlyList<ModelComparisonCaseValue> Candidates);

public sealed record ModelComparisonReport(
    string Version,
    string WorkspaceId,
    string BaselineLabel,
    int Cases,
    IReadOnlyList<ModelComparisonCandidateSummary> Candidates,
    IReadOnlyList<ModelComparisonCaseRow> PerCase);

public sealed class ModelComparisonValidationException(string message)
    : Exception(message);
