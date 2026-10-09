namespace PersonalAI.Web.Services;

public enum LocalVisualProviderHealthState
{
    Healthy,
    Degraded,
    Unavailable
}

public sealed record LocalVisualProviderHealth(
    string Provider,
    LocalVisualProviderHealthState State,
    int ConsecutiveFailures,
    long LastLatencyMilliseconds,
    double AverageLatencyMilliseconds,
    DateTimeOffset UpdatedAtUtc,
    string Reason);

public interface ILocalVisualProviderHealthRegistry
{
    LocalVisualProviderHealth Get(
        string provider);

    void RecordSuccess(
        string provider,
        long latencyMilliseconds,
        string reason = "");

    void RecordFailure(
        string provider,
        long latencyMilliseconds,
        string reason);

    bool ShouldSkip(
        string provider,
        DateTimeOffset nowUtc,
        out string reason);
}

public sealed class LocalVisualProviderHealthRegistry
    : ILocalVisualProviderHealthRegistry
{
    private sealed record Entry(
        int ConsecutiveFailures,
        long LastLatencyMilliseconds,
        double AverageLatencyMilliseconds,
        DateTimeOffset UpdatedAtUtc,
        string Reason);

    private readonly object gate =
        new();

    private readonly Dictionary<string, Entry> entries =
        new(
            StringComparer.OrdinalIgnoreCase);

    public LocalVisualProviderHealth Get(
        string provider)
    {
        provider =
            Normalize(
                provider);

        lock (gate)
        {
            if (!entries.TryGetValue(
                    provider,
                    out var entry))
            {
                return new(
                    provider,
                    LocalVisualProviderHealthState.Healthy,
                    0,
                    0,
                    0,
                    DateTimeOffset.MinValue,
                    "Chưa có failure.");
            }

            return ToPublic(
                provider,
                entry,
                DateTimeOffset.UtcNow);
        }
    }

    public void RecordSuccess(
        string provider,
        long latencyMilliseconds,
        string reason = "")
    {
        provider =
            Normalize(
                provider);

        lock (gate)
        {
            entries.TryGetValue(
                provider,
                out var previous);

            var latency =
                Math.Max(
                    0,
                    latencyMilliseconds);

            entries[provider] =
                new(
                    ConsecutiveFailures: 0,
                    LastLatencyMilliseconds:
                        latency,
                    AverageLatencyMilliseconds:
                        ComputeAverageLatency(
                            previous?.AverageLatencyMilliseconds ?? 0,
                            latency),
                    UpdatedAtUtc:
                        DateTimeOffset.UtcNow,
                    Reason:
                        string.IsNullOrWhiteSpace(reason)
                            ? "Provider thành công."
                            : reason.Trim());
        }
    }

    public void RecordFailure(
        string provider,
        long latencyMilliseconds,
        string reason)
    {
        provider =
            Normalize(
                provider);

        lock (gate)
        {
            entries.TryGetValue(
                provider,
                out var previous);

            var latency =
                Math.Max(
                    0,
                    latencyMilliseconds);

            entries[provider] =
                new(
                    ConsecutiveFailures:
                        Math.Min(
                            20,
                            (previous?.ConsecutiveFailures ?? 0) +
                            1),
                    LastLatencyMilliseconds:
                        latency,
                    AverageLatencyMilliseconds:
                        ComputeAverageLatency(
                            previous?.AverageLatencyMilliseconds ?? 0,
                            latency),
                    UpdatedAtUtc:
                        DateTimeOffset.UtcNow,
                    Reason:
                        string.IsNullOrWhiteSpace(reason)
                            ? "Provider thất bại."
                            : reason.Trim());
        }
    }

    public bool ShouldSkip(
        string provider,
        DateTimeOffset nowUtc,
        out string reason)
    {
        provider =
            Normalize(
                provider);

        lock (gate)
        {
            if (!entries.TryGetValue(
                    provider,
                    out var entry) ||
                entry.ConsecutiveFailures <
                    3)
            {
                reason =
                    string.Empty;
                return false;
            }

            var cooldown =
                entry.ConsecutiveFailures >= 6
                    ? TimeSpan.FromSeconds(30)
                    : TimeSpan.FromSeconds(10);

            var remaining =
                cooldown -
                (nowUtc -
                 entry.UpdatedAtUtc);

            if (remaining <=
                TimeSpan.Zero)
            {
                reason =
                    string.Empty;
                return false;
            }

            reason =
                $"{provider} đang cooldown do {entry.ConsecutiveFailures} failure liên tiếp; còn {Math.Ceiling(remaining.TotalSeconds):0}s.";
            return true;
        }
    }

    private static LocalVisualProviderHealth ToPublic(
        string provider,
        Entry entry,
        DateTimeOffset nowUtc)
    {
        var state =
            entry.ConsecutiveFailures >= 3 &&
            nowUtc -
                entry.UpdatedAtUtc <
                TimeSpan.FromSeconds(
                    entry.ConsecutiveFailures >= 6
                        ? 30
                        : 10)
                ? LocalVisualProviderHealthState.Unavailable
                : entry.ConsecutiveFailures > 0 ||
                  entry.LastLatencyMilliseconds >
                    5000
                    ? LocalVisualProviderHealthState.Degraded
                    : LocalVisualProviderHealthState.Healthy;

        return new(
            provider,
            state,
            entry.ConsecutiveFailures,
            entry.LastLatencyMilliseconds,
            entry.AverageLatencyMilliseconds,
            entry.UpdatedAtUtc,
            entry.Reason);
    }

    private static double ComputeAverageLatency(
        double previousAverage,
        long currentLatency)
    {
        if (previousAverage <= 0)
            return currentLatency;

        const double alpha = 0.25;

        return
            alpha *
                currentLatency +
            (1 - alpha) *
                previousAverage;
    }

    private static string Normalize(
        string provider) =>
        string.IsNullOrWhiteSpace(provider)
            ? "unknown"
            : provider.Trim().ToLowerInvariant();
}
