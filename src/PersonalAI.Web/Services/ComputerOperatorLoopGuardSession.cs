namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorLoopAssessment(
    bool Detected,
    bool RequiresStrategyChange,
    string Kind,
    string Detail,
    int Occurrences);

public sealed class ComputerOperatorLoopGuardSession
{
    private const int MaximumObservations = 16;

    private sealed record Observation(
        string State,
        string Strategy);

    private sealed record OutcomeObservation(
        string State,
        string Strategy,
        string Outcome);

    private readonly List<Observation> observations = [];
    private readonly List<OutcomeObservation> outcomes = [];
    private int warningCount;

    public ComputerOperatorLoopAssessment Observe(
        string state,
        string strategy)
    {
        var normalizedState = Normalize(state, 320);
        var normalizedStrategy = Normalize(strategy, 260);

        if (normalizedState.Length == 0 &&
            normalizedStrategy.Length == 0)
            return None();

        observations.Add(
            new Observation(
                normalizedState,
                normalizedStrategy));

        if (observations.Count > MaximumObservations)
            observations.RemoveRange(
                0,
                observations.Count - MaximumObservations);

        var repeatedStrategy = DetectRepeatedStrategy();
        if (repeatedStrategy.Detected)
            return WithWarning(repeatedStrategy);

        var stagnation = DetectStagnation();
        if (stagnation.Detected)
            return WithWarning(stagnation);

        var oscillation = DetectOscillation();
        if (oscillation.Detected)
            return WithWarning(oscillation);

        warningCount = Math.Max(
            0,
            warningCount - 1);

        return None();
    }

    public ComputerOperatorLoopAssessment ObserveOutcome(
        string state,
        string strategy,
        string outcome)
    {
        var normalizedState = Normalize(state, 320);
        var normalizedStrategy = Normalize(strategy, 260);
        var normalizedOutcome = Normalize(outcome, 320);

        if (normalizedStrategy.Length == 0 ||
            normalizedOutcome.Length == 0)
            return None();

        outcomes.Add(
            new OutcomeObservation(
                normalizedState,
                normalizedStrategy,
                normalizedOutcome));

        if (outcomes.Count > MaximumObservations)
            outcomes.RemoveRange(
                0,
                outcomes.Count - MaximumObservations);

        var sameFingerprint = outcomes
            .Where(item =>
                item.Strategy.Equals(
                    normalizedStrategy,
                    StringComparison.OrdinalIgnoreCase) &&
                item.Outcome.Equals(
                    normalizedOutcome,
                    StringComparison.OrdinalIgnoreCase) &&
                StatesEquivalent(
                    item.State,
                    normalizedState))
            .ToArray();

        if (sameFingerprint.Length >= 2)
        {
            warningCount++;

            return new(
                true,
                true,
                "repeated-outcome",
                sameFingerprint.Length >= 3
                    ? "Cùng scene + action + outcome thất bại đã lặp ít nhất 3 lần. Cấm retry chiến lược này; phải đổi phương án hoặc dừng an toàn."
                    : "Cùng scene + action + outcome thất bại đã lặp lại. Không được retry y hệt; phải đổi chiến lược.",
                warningCount);
        }

        return None();
    }

    public void MarkProgress()
    {
        warningCount = 0;
        outcomes.Clear();

        if (observations.Count > 1)
        {
            var last = observations[^1];
            observations.Clear();
            observations.Add(last);
        }
    }

    private ComputerOperatorLoopAssessment DetectRepeatedStrategy()
    {
        if (observations.Count < 3)
            return None();

        var recent = observations.TakeLast(3).ToArray();
        var strategy = recent[0].Strategy;

        if (strategy.Length == 0 ||
            recent.Any(item =>
                !item.Strategy.Equals(
                    strategy,
                    StringComparison.OrdinalIgnoreCase)))
            return None();

        return new(
            true,
            true,
            "repeated-strategy",
            "AI đang lặp cùng một chiến lược hành động. Phải chọn một chiến lược khác từ trạng thái hiện tại; nếu không còn lựa chọn an toàn hợp lý thì trả blocked.",
            3);
    }

    private ComputerOperatorLoopAssessment DetectStagnation()
    {
        if (observations.Count < 4)
            return None();

        var recent = observations.TakeLast(4).ToArray();
        var state = recent[0].State;

        if (state.Length == 0 ||
            recent.Any(item =>
                !item.State.Equals(
                    state,
                    StringComparison.OrdinalIgnoreCase)))
            return None();

        var currentStrategy = recent[^1].Strategy;
        var strategySeenBefore =
            currentStrategy.Length > 0 &&
            recent
                .Take(recent.Length - 1)
                .Any(item =>
                    item.Strategy.Equals(
                        currentStrategy,
                        StringComparison.OrdinalIgnoreCase));

        return new(
            true,
            strategySeenBefore,
            "stagnation",
            strategySeenBefore
                ? "Desktop gần như không đổi và chiến lược hiện tại đã được thử trong trạng thái này. Phải đổi chiến lược."
                : "Desktop gần như không đổi, nhưng chiến lược hiện tại là phương án mới; có thể thử rồi phải xác minh kết quả.",
            4);
    }

    private ComputerOperatorLoopAssessment DetectOscillation()
    {
        if (observations.Count < 4)
            return None();

        var recent = observations.TakeLast(4).ToArray();
        var a = recent[0].State;
        var b = recent[1].State;

        if (a.Length == 0 ||
            b.Length == 0 ||
            a.Equals(
                b,
                StringComparison.OrdinalIgnoreCase))
            return None();

        var oscillates =
            recent[2].State.Equals(
                a,
                StringComparison.OrdinalIgnoreCase) &&
            recent[3].State.Equals(
                b,
                StringComparison.OrdinalIgnoreCase);

        if (!oscillates)
            return None();

        var currentStrategy = recent[^1].Strategy;
        var strategySeenBefore =
            currentStrategy.Length > 0 &&
            recent
                .Take(recent.Length - 1)
                .Any(item =>
                    item.Strategy.Equals(
                        currentStrategy,
                        StringComparison.OrdinalIgnoreCase));

        return new(
            true,
            strategySeenBefore,
            "oscillation",
            strategySeenBefore
                ? "Desktop đang dao động A ↔ B và chiến lược hiện tại đã xuất hiện trong chuỗi. Phải chọn cách tiếp cận khác."
                : "Desktop đang dao động A ↔ B; chiến lược mới được phép thử một lần rồi phải xác minh.",
            2);
    }

    private ComputerOperatorLoopAssessment WithWarning(
        ComputerOperatorLoopAssessment assessment)
    {
        warningCount++;
        return assessment with
        {
            Occurrences = warningCount
        };
    }

    private static ComputerOperatorLoopAssessment None() =>
        new(
            false,
            false,
            string.Empty,
            string.Empty,
            0);

    private static bool StatesEquivalent(
        string left,
        string right)
    {
        if (left.Equals(
                right,
                StringComparison.OrdinalIgnoreCase))
            return true;

        if (left.Length >= 24 &&
            right.Length >= 24 &&
            (left.Contains(
                 right,
                 StringComparison.OrdinalIgnoreCase) ||
             right.Contains(
                 left,
                 StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private static string Normalize(
        string value,
        int maximum)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Split(
                    [' ', '\t', '\r', '\n', '.', ',', ';', ':'],
                    StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= maximum
            ? normalized
            : normalized[..maximum];
    }
}
