namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorRegressionReadinessGateResult(
    string Version,
    DateTimeOffset EvaluatedAtUtc,
    bool Passed,
    string Status,
    IReadOnlyList<string> Reasons,
    ComputerOperatorRegressionMetrics Metrics);

public interface IComputerOperatorRegressionReadinessGate
{
    ComputerOperatorRegressionReadinessGateResult Evaluate();
}

public sealed class ComputerOperatorRegressionReadinessGate(
    IComputerOperatorRegressionMetricsService metrics)
    : IComputerOperatorRegressionReadinessGate
{
    public ComputerOperatorRegressionReadinessGateResult Evaluate()
    {
        var snapshot = metrics.Get();
        return EvaluateSnapshot(snapshot);
    }

    internal static ComputerOperatorRegressionReadinessGateResult EvaluateForAcceptance(
        ComputerOperatorRegressionMetrics metrics) =>
        EvaluateSnapshot(metrics);

    private static ComputerOperatorRegressionReadinessGateResult EvaluateSnapshot(
        ComputerOperatorRegressionMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        var reasons = new List<string>();

        if (!metrics.BaselineCoverageHealthy)
        {
            reasons.Add(
                $"Regression baseline chưa đạt: {metrics.LabPassedCases}/{metrics.LabTotalCases} pass, failed={metrics.LabFailedCases}.");
        }

        if (metrics.LabPassRate < 1d)
        {
            reasons.Add(
                $"Regression pass rate phải đạt 100%, hiện tại {metrics.LabPassRate:P1}.");
        }

        if (metrics.RuntimeUnreviewedCandidates > 0)
        {
            reasons.Add(
                $"Còn {metrics.RuntimeUnreviewedCandidates} runtime regression candidate chưa review/promote.");
        }

        if (!metrics.ReadyForExternalBenchmark)
        {
            reasons.Add(
                "Regression metrics chưa đánh dấu ReadyForExternalBenchmark.");
        }

        var passed =
            reasons.Count == 0;

        return new(
            metrics.Version,
            DateTimeOffset.UtcNow,
            passed,
            passed ? "pass" : "blocked",
            reasons,
            metrics);
    }
}
