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
