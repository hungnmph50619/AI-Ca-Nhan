using System.Security.Cryptography;
using System.Text;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorRegressionCandidate(
    string Id,
    DateTimeOffset CapturedAtUtc,
    string GoalHash,
    string Outcome,
    string Summary,
    string CurrentStage,
    int ObservationCount,
    int ActionCount,
    string Provider,
    string Model,
    IReadOnlyList<string> FailureSignals);

public sealed record ComputerOperatorRegressionCandidateSnapshot(
    int Count,
    int MaximumEntries,
    IReadOnlyList<ComputerOperatorRegressionCandidate> Candidates);

public sealed record ComputerOperatorRegressionIncidentDraft(
    string Id,
    string Title,
    string Severity,
    string Origin,
    string RegressionCaseId,
    string ExpectedInvariant);

public sealed record ComputerOperatorRegressionTestDraft(
    string Id,
    string Category,
    string Outcome,
    string CurrentStage,
    IReadOnlyList<string> FailureSignals,
    string ExpectedBehavior);

public sealed record ComputerOperatorRegressionPromotionDraft(
    string CandidateId,
    DateTimeOffset CreatedAtUtc,
    bool RequiresReview,
    ComputerOperatorRegressionIncidentDraft Incident,
    ComputerOperatorRegressionTestDraft Test);

public interface IComputerOperatorRegressionCandidateStore
{
    void Capture(
        string goal,
        string outcome,
        string summary,
        ComputerOperatorProgressSnapshot progress,
        string provider,
        string model);

    ComputerOperatorRegressionCandidateSnapshot Get();

    ComputerOperatorRegressionPromotionDraft? CreateDraft(
        string candidateId);
}

public sealed class ComputerOperatorRegressionCandidateStore
    : IComputerOperatorRegressionCandidateStore
{
    public const int MaximumEntries = 50;
    private const int MaximumSummaryCharacters = 320;
    private const int MaximumSignals = 12;

    private readonly object _gate = new();
    private readonly List<ComputerOperatorRegressionCandidate> _candidates = [];

    public void Capture(
        string goal,
        string outcome,
        string summary,
        ComputerOperatorProgressSnapshot progress,
        string provider,
        string model)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var normalizedOutcome =
            string.IsNullOrWhiteSpace(outcome)
                ? "failed"
                : outcome.Trim().ToLowerInvariant();

        var signals =
            progress.Entries
                .Where(entry =>
                    entry.Stage.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                    entry.Stage.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
                    entry.Stage.Contains("failure", StringComparison.OrdinalIgnoreCase) ||
                    entry.Stage.Contains("replan", StringComparison.OrdinalIgnoreCase))
                .Select(entry => NormalizeSignal(entry.Stage))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumSignals)
                .ToArray();

        if (signals.Length == 0 &&
            normalizedOutcome is not ("blocked" or "failed"))
            return;

        var capturedAt = DateTimeOffset.UtcNow;
        var goalHash = HashGoal(goal);
        var id =
            $"runtime-{capturedAt:yyyyMMddHHmmssfff}-{goalHash[..12]}";

        var candidate =
            new ComputerOperatorRegressionCandidate(
                id,
                capturedAt,
                goalHash,
                normalizedOutcome,
                Limit(summary, MaximumSummaryCharacters),
                Limit(progress.CurrentStage, 80),
                progress.ObservationCount,
                progress.ActionCount,
                Limit(provider, 80),
                Limit(model, 120),
                signals);

        lock (_gate)
        {
            _candidates.Add(candidate);

            if (_candidates.Count > MaximumEntries)
                _candidates.RemoveRange(
                    0,
                    _candidates.Count - MaximumEntries);
        }
    }

    public ComputerOperatorRegressionCandidateSnapshot Get()
    {
        lock (_gate)
        {
            return new(
                _candidates.Count,
                MaximumEntries,
                _candidates
                    .OrderByDescending(item => item.CapturedAtUtc)
                    .ToArray());
        }
    }

    public ComputerOperatorRegressionPromotionDraft? CreateDraft(
        string candidateId)
    {
        var id = (candidateId ?? string.Empty).Trim();
        if (id.Length == 0)
            return null;

        ComputerOperatorRegressionCandidate? candidate;
        lock (_gate)
        {
            candidate =
                _candidates.FirstOrDefault(item =>
                    item.Id.Equals(
                        id,
                        StringComparison.Ordinal));
        }

        if (candidate is null)
            return null;

        var primarySignal =
            candidate.FailureSignals.FirstOrDefault() ??
            candidate.CurrentStage ??
            "runtime-failure";

        var slug =
            Slug(primarySignal);

        var caseId =
            $"runtime-{slug}-{candidate.GoalHash[..12]}";

        var severity =
            candidate.Outcome.Equals(
                "blocked",
                StringComparison.OrdinalIgnoreCase) ||
            candidate.FailureSignals.Any(signal =>
                signal.Contains(
                    "verification-failed",
                    StringComparison.OrdinalIgnoreCase))
                ? "high"
                : "medium";

        var title =
            $"Runtime {candidate.Outcome}: {primarySignal}";

        return new(
            candidate.Id,
            DateTimeOffset.UtcNow,
            RequiresReview: true,
            new(
                $"incident-{caseId}",
                title,
                severity,
                "runtime Computer Operator regression candidate",
                caseId,
                $"Computer Operator không được tái diễn failure signature '{primarySignal}' trong điều kiện tương đương."),
            new(
                caseId,
                "computer-operator.runtime-regression",
                candidate.Outcome,
                candidate.CurrentStage,
                candidate.FailureSignals,
                $"Luồng tương đương phải hoàn thành hoặc chuyển sang recovery an toàn mà không lặp failure signature '{primarySignal}'."));
    }

    internal static string HashGoalForAcceptance(string goal) =>
        HashGoal(goal);

    private static string HashGoal(string goal)
    {
        var normalized = (goal ?? string.Empty).Trim();
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexString(bytes)
            .ToLowerInvariant();
    }

    private static string NormalizeSignal(string value) =>
        (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

    private static string Slug(string value)
    {
        var normalized =
            new string(
                NormalizeSignal(value)
                    .Select(character =>
                        char.IsLetterOrDigit(character)
                            ? character
                            : '-')
                    .ToArray());

        while (normalized.Contains(
                   "--",
                   StringComparison.Ordinal))
            normalized =
                normalized.Replace(
                    "--",
                    "-",
                    StringComparison.Ordinal);

        normalized =
            normalized.Trim('-');

        if (normalized.Length == 0)
            normalized = "runtime-failure";

        return normalized.Length <= 48
            ? normalized
            : normalized[..48].TrimEnd('-');
    }

    private static string Limit(
        string? value,
        int maximumCharacters)
    {
        var normalized =
            (value ?? string.Empty)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();

        return normalized.Length <= maximumCharacters
            ? normalized
            : normalized[..maximumCharacters];
    }
}
