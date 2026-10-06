using PersonalAI.Web.Evaluation.Regression;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorRegressionMetrics(
    string Version,
    DateTimeOffset MeasuredAtUtc,
    int LabTotalCases,
    int LabPassedCases,
    int LabFailedCases,
    double LabPassRate,
    int RuntimeCandidateCount,
    int RuntimePromotedCases,
    int RuntimeUnreviewedCandidates,
    double RuntimePromotionRate,
    bool BaselineCoverageHealthy,
    bool ReadyForExternalBenchmark,
    IReadOnlyList<string> UnreviewedCandidateIds);

public interface IComputerOperatorRegressionMetricsService
{
    ComputerOperatorRegressionMetrics Get();
}

public sealed class ComputerOperatorRegressionMetricsService(
    IComputerOperatorRegressionLabService lab,
    IComputerOperatorRegressionCandidateStore candidates,
    IRegressionDatasetStore regressionStore)
    : IComputerOperatorRegressionMetricsService
{
    public ComputerOperatorRegressionMetrics Get()
    {
        var labReport = lab.Run();
        var candidateSnapshot = candidates.Get();
        var promotedIds =
            regressionStore.GetAll()
                .Where(item =>
                    item.Enabled &&
                    string.Equals(
                        item.Category,
                        ComputerOperatorRegressionPromotionService.RuntimeRegressionCategory,
                        StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);

        return Calculate(
            labReport,
            candidateSnapshot,
            promotedIds);
    }

    internal static ComputerOperatorRegressionMetrics CalculateForAcceptance(
        ComputerOperatorRegressionLabReport labReport,
        ComputerOperatorRegressionCandidateSnapshot candidateSnapshot,
        IReadOnlySet<string> promotedIds) =>
        Calculate(
            labReport,
            candidateSnapshot,
            promotedIds);

    private static ComputerOperatorRegressionMetrics Calculate(
        ComputerOperatorRegressionLabReport labReport,
        ComputerOperatorRegressionCandidateSnapshot candidateSnapshot,
        IReadOnlySet<string> promotedIds)
    {
        ArgumentNullException.ThrowIfNull(labReport);
        ArgumentNullException.ThrowIfNull(candidateSnapshot);
        ArgumentNullException.ThrowIfNull(promotedIds);

        var unreviewed =
            candidateSnapshot.Candidates
                .Where(candidate =>
                {
                    var draft =
                        CreateDraftId(
                            candidate);

                    return !promotedIds.Contains(
                        draft);
                })
                .Select(candidate => candidate.Id)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

        var promotedCandidateCount =
            Math.Max(
                0,
                candidateSnapshot.Count - unreviewed.Length);

        var promotionRate =
            candidateSnapshot.Count == 0
                ? 1d
                : (double)promotedCandidateCount /
                  candidateSnapshot.Count;

        var baselineHealthy =
            labReport.TotalCases >= 6 &&
            labReport.FailedCases == 0 &&
            labReport.PassedCases == labReport.TotalCases &&
            labReport.PassRate >= 1d;

        var ready =
            baselineHealthy &&
            unreviewed.Length == 0;

        return new(
            labReport.Version,
            DateTimeOffset.UtcNow,
            labReport.TotalCases,
            labReport.PassedCases,
            labReport.FailedCases,
            labReport.PassRate,
            candidateSnapshot.Count,
            promotedIds.Count,
            unreviewed.Length,
            promotionRate,
            baselineHealthy,
            ready,
            unreviewed);
    }

    private static string CreateDraftId(
        ComputerOperatorRegressionCandidate candidate)
    {
        var primarySignal =
            candidate.FailureSignals.FirstOrDefault() ??
            candidate.CurrentStage ??
            "runtime-failure";

        var slug =
            new string(
                primarySignal
                    .Trim()
                    .ToLowerInvariant()
                    .Select(character =>
                        char.IsLetterOrDigit(character)
                            ? character
                            : '-')
                    .ToArray());

        while (slug.Contains(
                   "--",
                   StringComparison.Ordinal))
            slug =
                slug.Replace(
                    "--",
                    "-",
                    StringComparison.Ordinal);

        slug = slug.Trim('-');

        if (slug.Length == 0)
            slug = "runtime-failure";

        if (slug.Length > 48)
            slug = slug[..48].TrimEnd('-');

        return $"runtime-{slug}-{candidate.GoalHash[..12]}";
    }
}
