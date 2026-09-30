namespace PersonalAI.Web.ModelLab;

public sealed record RegisteredModelBenchmarkMetrics(
    string BenchmarkId,
    string BenchmarkSha256,
    int Cases,
    double Accuracy,
    double TaskSuccessRate,
    double AverageLatencyMilliseconds,
    double AverageCostPerCase,
    double RegressionPassRate);

public sealed record CompareRegisteredModelsRequest(
    Guid CandidateModelVersionId,
    Guid? ProductionModelVersionId,
    RegisteredModelBenchmarkMetrics CandidateMetrics,
    RegisteredModelBenchmarkMetrics ProductionMetrics);

public sealed record RegisteredModelMetricDelta(
    double Accuracy,
    double TaskSuccessRate,
    double AverageLatencyMilliseconds,
    double AverageCostPerCase,
    double RegressionPassRate);

public sealed record RegisteredModelComparisonReport(
    Guid Id,
    string WorkspaceId,
    string Family,
    Guid CandidateModelVersionId,
    string CandidateVersion,
    Guid ProductionModelVersionId,
    string ProductionVersion,
    RegisteredModelBenchmarkMetrics Candidate,
    RegisteredModelBenchmarkMetrics Production,
    RegisteredModelMetricDelta DeltaCandidateMinusProduction,
    string InputSha256,
    bool ReproducibleInputs,
    bool ProductionMutationPerformed,
    DateTimeOffset CreatedAt);

public sealed record RegisteredModelComparisonStatus(
    string Version,
    string WorkspaceId,
    int Reports,
    bool ProductionBaselineRequired,
    bool SameBenchmarkRequired,
    bool AutoPromotionEnabled,
    bool Persistent);

public sealed class RegisteredModelComparisonValidationException(string message)
    : Exception(message);
