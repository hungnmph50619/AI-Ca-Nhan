namespace PersonalAI.Web.Evaluation.Core;

/// <summary>
/// Canonical v2.3 metrics from the project roadmap. Evaluators may publish
/// these keys when the corresponding measurement is available.
/// </summary>
public static class EvaluationMetricCatalog
{
    public const string MemoryAccuracy = "memory.accuracy";
    public const string RagAccuracy = "rag.accuracy";
    public const string ToolSuccessRate = "tool.success_rate";
    public const string DesktopSuccessRate = "desktop.success_rate";
    public const string BrowserSuccessRate = "browser.success_rate";
    public const string CodeSuccessRate = "code.success_rate";
    public const string TaskSuccessRate = "task.success_rate";
    public const string LatencyMilliseconds = "latency.ms";
    public const string Cost = "cost";

    public static readonly IReadOnlyList<string> RoadmapMetrics =
    [
        MemoryAccuracy,
        RagAccuracy,
        ToolSuccessRate,
        DesktopSuccessRate,
        BrowserSuccessRate,
        CodeSuccessRate,
        TaskSuccessRate,
        LatencyMilliseconds,
        Cost
    ];
}

public sealed record EvaluationMetricSummary(
    string Name,
    int Samples,
    double Average,
    double Minimum,
    double Maximum);

public sealed record EvaluationCategorySummary(
    string Category,
    int Cases,
    double AverageScore,
    double MinimumScore,
    double MaximumScore,
    int ErrorCount,
    IReadOnlyDictionary<string, int> ErrorDistribution,
    IReadOnlyList<EvaluationMetricSummary> Metrics);

public sealed record EvaluationSummary(
    int Cases,
    double AverageScore,
    double MinimumScore,
    double MaximumScore,
    int ErrorCount,
    IReadOnlyDictionary<string, int> ErrorDistribution,
    IReadOnlyList<EvaluationMetricSummary> Metrics,
    IReadOnlyList<EvaluationCategorySummary> Categories);

/// <summary>
/// Aggregates deterministic evaluator output. It does not call an AI provider
/// and does not invent unavailable metrics.
/// </summary>
public static class EvaluationMetricsAggregator
{
    public const int MaximumResults = 1000;

    public static EvaluationSummary Summarize(IReadOnlyList<EvaluationResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0)
            throw new EvaluationEngineException("At least one evaluation result is required.");
        if (results.Count > MaximumResults)
            throw new EvaluationEngineException(
                $"Evaluation summary cannot exceed {MaximumResults} results.");

        Validate(results);

        var categorySummaries = results
            .GroupBy(result => result.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildCategory(group.Key, group.ToArray()))
            .ToArray();

        return new EvaluationSummary(
            Cases: results.Count,
            AverageScore: results.Average(static result => result.Score),
            MinimumScore: results.Min(static result => result.Score),
            MaximumScore: results.Max(static result => result.Score),
            ErrorCount: results.Sum(static result => result.Errors.Count),
            ErrorDistribution: BuildErrorDistribution(results),
            Metrics: BuildMetricSummaries(results),
            Categories: categorySummaries);
    }

    private static EvaluationCategorySummary BuildCategory(
        string category,
        IReadOnlyList<EvaluationResult> results) =>
        new(
            Category: category,
            Cases: results.Count,
            AverageScore: results.Average(static result => result.Score),
            MinimumScore: results.Min(static result => result.Score),
            MaximumScore: results.Max(static result => result.Score),
            ErrorCount: results.Sum(static result => result.Errors.Count),
            ErrorDistribution: BuildErrorDistribution(results),
            Metrics: BuildMetricSummaries(results));

    private static IReadOnlyDictionary<string, int> BuildErrorDistribution(
        IEnumerable<EvaluationResult> results) =>
        results
            .SelectMany(static result => result.Errors)
            .GroupBy(static error => error, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<EvaluationMetricSummary> BuildMetricSummaries(
        IEnumerable<EvaluationResult> results)
    {
        var values = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
        {
            if (result.Metrics is null)
                continue;

            foreach (var metric in result.Metrics)
            {
                if (!values.TryGetValue(metric.Key, out var bucket))
                {
                    bucket = [];
                    values.Add(metric.Key, bucket);
                }
                bucket.Add(metric.Value);
            }
        }

        return values
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new EvaluationMetricSummary(
                pair.Key,
                pair.Value.Count,
                pair.Value.Average(),
                pair.Value.Min(),
                pair.Value.Max()))
            .ToArray();
    }

    private static void Validate(IReadOnlyList<EvaluationResult> results)
    {
        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            if (result is null)
                throw new EvaluationEngineException("Evaluation result cannot be null.");
            if (string.IsNullOrWhiteSpace(result.CaseId) ||
                string.IsNullOrWhiteSpace(result.Category))
                throw new EvaluationEngineException(
                    "Evaluation result must contain caseId and category.");
            if (!caseIds.Add(result.CaseId))
                throw new EvaluationEngineException(
                    $"Duplicate evaluation result case ID '{result.CaseId}'.");
            if (!double.IsFinite(result.Score))
                throw new EvaluationEngineException(
                    $"Evaluation result '{result.CaseId}' has a non-finite score.");

            if (result.Metrics is null)
                continue;

            foreach (var metric in result.Metrics)
            {
                if (string.IsNullOrWhiteSpace(metric.Key) ||
                    !double.IsFinite(metric.Value))
                    throw new EvaluationEngineException(
                        $"Evaluation result '{result.CaseId}' has an invalid metric.");
            }
        }
    }
}
