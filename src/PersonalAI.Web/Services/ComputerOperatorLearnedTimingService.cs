namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorTimingProfile(
    string Stage,
    string Action,
    int SampleCount,
    double MedianMilliseconds,
    double P90Milliseconds,
    double P95Milliseconds,
    double MaximumMilliseconds,
    double Confidence,
    bool Ready);

public interface IComputerOperatorLearnedTimingService
{
    ComputerOperatorTimingProfile GetProfile(
        string stage,
        string action);

    IReadOnlyList<ComputerOperatorTimingProfile> GetProfiles();
}

public sealed class ComputerOperatorLearnedTimingService(
    IComputerOperatorTelemetry telemetry,
    IComputerOperatorExperienceAggregateStore aggregates)
    : IComputerOperatorLearnedTimingService
{
    private readonly object persistenceGate = new();
    private readonly Dictionary<string, int> persistedSessionSamples =
        new(StringComparer.OrdinalIgnoreCase);
    public const int MinimumReadySamples = 5;
    public const int StrongConfidenceSamples = 20;

    public ComputerOperatorTimingProfile GetProfile(
        string stage,
        string action)
    {
        var normalizedStage = Normalize(stage);
        var normalizedAction = Normalize(action);

        var durations =
            telemetry.GetSnapshot()
                .RecentEvents
                .Where(item =>
                    item.Success &&
                    item.Stage.Equals(
                        normalizedStage,
                        StringComparison.OrdinalIgnoreCase) &&
                    item.Action.Equals(
                        normalizedAction,
                        StringComparison.OrdinalIgnoreCase))
                .Select(item =>
                    (double)item.DurationMilliseconds)
                .OrderBy(value => value)
                .ToArray();

        var current =
            Build(
                normalizedStage,
                normalizedAction,
                durations);

        return MergeWithPersistent(
            current);
    }

    public IReadOnlyList<ComputerOperatorTimingProfile> GetProfiles()
    {
        var current =
            telemetry.GetSnapshot()
                .RecentEvents
                .Where(item => item.Success)
                .GroupBy(
                    item => new
                    {
                        Stage = item.Stage.ToLowerInvariant(),
                        Action = item.Action.ToLowerInvariant()
                    })
                .Select(group =>
                    Build(
                        group.Key.Stage,
                        group.Key.Action,
                        group.Select(item =>
                                (double)item.DurationMilliseconds)
                            .OrderBy(value => value)
                            .ToArray()))
                .ToDictionary(
                    item => Key(item.Stage, item.Action),
                    StringComparer.OrdinalIgnoreCase);

        var persistent =
            aggregates.GetTimingProfiles();

        var keys =
            current.Keys
                .Concat(
                    persistent.Select(item =>
                        Key(item.Stage, item.Action)))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return keys
            .Select(key =>
            {
                if (current.TryGetValue(
                        key,
                        out var currentProfile))
                {
                    return MergeWithPersistent(
                        currentProfile);
                }

                var stored =
                    persistent.First(item =>
                        Key(item.Stage, item.Action)
                            .Equals(
                                key,
                                StringComparison.OrdinalIgnoreCase));

                return FromPersistent(
                    stored);
            })
            .OrderByDescending(item => item.SampleCount)
            .ThenBy(item => item.Stage, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Action, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private ComputerOperatorTimingProfile MergeWithPersistent(
        ComputerOperatorTimingProfile current)
    {
        var stored =
            aggregates.GetTimingProfile(
                current.Stage,
                current.Action);

        if (current.SampleCount == 0)
        {
            return stored is null
                ? current
                : FromPersistent(
                    stored);
        }

        var key =
            Key(
                current.Stage,
                current.Action);

        var alreadyPersistedThisSession =
            0;

        lock (persistenceGate)
        {
            persistedSessionSamples.TryGetValue(
                key,
                out alreadyPersistedThisSession);
        }

        var newSessionSamples =
            Math.Max(
                0,
                current.SampleCount -
                alreadyPersistedThisSession);

        var merged =
            stored is null
                ? current
                : MergeProfiles(
                    FromPersistent(stored),
                    current,
                    newSessionSamples);

        if (newSessionSamples > 0)
        {
            aggregates.UpsertTimingProfile(
                new(
                    merged.Stage,
                    merged.Action,
                    merged.SampleCount,
                    merged.MedianMilliseconds,
                    merged.P90Milliseconds,
                    merged.P95Milliseconds,
                    merged.MaximumMilliseconds,
                    merged.Confidence,
                    DateTimeOffset.UtcNow));

            lock (persistenceGate)
            {
                persistedSessionSamples[key] =
                    current.SampleCount;
            }
        }

        return merged;
    }

    private static ComputerOperatorTimingProfile MergeProfiles(
        ComputerOperatorTimingProfile stored,
        ComputerOperatorTimingProfile current,
        int newSessionSamples)
    {
        if (newSessionSamples <= 0)
            return stored;

        var storedWeight =
            Math.Max(
                0,
                stored.SampleCount);

        var total =
            storedWeight +
            newSessionSamples;

        if (total <= 0)
            return current;

        static double Weighted(
            double left,
            int leftWeight,
            double right,
            int rightWeight,
            int totalWeight) =>
            ((left * leftWeight) +
             (right * rightWeight)) /
            Math.Max(
                1,
                totalWeight);

        return new(
            current.Stage,
            current.Action,
            total,
            Weighted(
                stored.MedianMilliseconds,
                storedWeight,
                current.MedianMilliseconds,
                newSessionSamples,
                total),
            Weighted(
                stored.P90Milliseconds,
                storedWeight,
                current.P90Milliseconds,
                newSessionSamples,
                total),
            Weighted(
                stored.P95Milliseconds,
                storedWeight,
                current.P95Milliseconds,
                newSessionSamples,
                total),
            Math.Max(
                stored.MaximumMilliseconds,
                current.MaximumMilliseconds),
            Math.Clamp(
                (double)total /
                StrongConfidenceSamples,
                0,
                1),
            Ready:
                total >=
                MinimumReadySamples);
    }

    private static ComputerOperatorTimingProfile FromPersistent(
        ComputerOperatorPersistentTimingProfile profile) =>
        new(
            profile.Stage,
            profile.Action,
            profile.SampleCount,
            profile.MedianMilliseconds,
            profile.P90Milliseconds,
            profile.P95Milliseconds,
            profile.MaximumMilliseconds,
            profile.Confidence,
            Ready:
                profile.SampleCount >=
                MinimumReadySamples);

    private static string Key(
        string stage,
        string action) =>
        $"{Normalize(stage)}|{Normalize(action)}";

    private static ComputerOperatorTimingProfile Build(
        string stage,
        string action,
        IReadOnlyList<double> sorted)
    {
        if (sorted.Count == 0)
        {
            return new(
                stage,
                action,
                0,
                0,
                0,
                0,
                0,
                0,
                Ready: false);
        }

        var confidence =
            Math.Clamp(
                (double)sorted.Count /
                StrongConfidenceSamples,
                0,
                1);

        return new(
            stage,
            action,
            sorted.Count,
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.90),
            Percentile(sorted, 0.95),
            sorted[^1],
            confidence,
            Ready:
                sorted.Count >=
                MinimumReadySamples);
    }

    internal static double Percentile(
        IReadOnlyList<double> sorted,
        double percentile)
    {
        ArgumentNullException.ThrowIfNull(sorted);

        if (sorted.Count == 0)
            return 0;

        var p =
            Math.Clamp(
                percentile,
                0,
                1);

        if (sorted.Count == 1)
            return sorted[0];

        var position =
            p * (sorted.Count - 1);

        var lower =
            (int)Math.Floor(position);
        var upper =
            (int)Math.Ceiling(position);

        if (lower == upper)
            return sorted[lower];

        var fraction =
            position - lower;

        return sorted[lower] +
            ((sorted[upper] - sorted[lower]) *
             fraction);
    }

    private static string Normalize(
        string? value)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        return normalized.Length == 0
            ? "none"
            : normalized;
    }
}

public interface IComputerOperatorAdaptiveWaitPolicyResolver
{
    AdaptiveWaitPolicy Resolve(
        string? action,
        string stage = ComputerOperatorTelemetryStages.AdaptiveWait);
}

public sealed class ComputerOperatorAdaptiveWaitPolicyResolver(
    IComputerOperatorLearnedTimingService learnedTiming)
    : IComputerOperatorAdaptiveWaitPolicyResolver
{
    public AdaptiveWaitPolicy Resolve(
        string? action,
        string stage = ComputerOperatorTelemetryStages.AdaptiveWait)
    {
        var fallback =
            ComputerOperatorAdaptiveWaitPolicy
                .ForAction(action);

        var profile =
            learnedTiming.GetProfile(
                stage,
                action ?? "none");

        if (!profile.Ready)
            return fallback;

        // P95 dùng làm baseline vì mục tiêu ưu tiên chính xác hơn tốc độ.
        // Margin 50% hấp thụ máy chậm tạm thời nhưng vẫn giữ absolute bound.
        var learnedAbsoluteMs =
            Math.Clamp(
                profile.P95Milliseconds * 1.5,
                fallback.AbsoluteTimeout.TotalMilliseconds,
                TimeSpan.FromMinutes(5).TotalMilliseconds);

        // Stall timeout phải nhỏ hơn absolute timeout và không ngắn hơn fallback.
        // Dùng median để tránh một outlier làm hệ thống chờ vô hạn.
        var learnedStallMs =
            Math.Clamp(
                profile.MedianMilliseconds * 0.75,
                fallback.StallTimeout.TotalMilliseconds,
                learnedAbsoluteMs * 0.75);

        return new(
            fallback.PollInterval,
            TimeSpan.FromMilliseconds(
                learnedStallMs),
            TimeSpan.FromMilliseconds(
                learnedAbsoluteMs),
            fallback.MinimumProgressConfidence);
    }
}
