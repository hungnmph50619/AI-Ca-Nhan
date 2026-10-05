using System.Collections.Concurrent;

namespace PersonalAI.Web.Services;

public sealed record UniversalCapabilityCacheStatus(
    long Generation,
    long Hits,
    long Misses,
    bool RuntimeSnapshotCached,
    DateTimeOffset? RuntimeExpiresAtUtc,
    int TemporaryUnavailabilityCount,
    string LastInvalidationReason);

public interface IUniversalCapabilityCache
{
    bool TryGetRuntimeSnapshot(
        out UniversalCapabilitySnapshot? snapshot);

    void SetRuntimeSnapshot(
        UniversalCapabilitySnapshot snapshot,
        TimeSpan timeToLive);

    UniversalCapabilitySnapshot ApplyHealthOverrides(
        UniversalCapabilitySnapshot snapshot);

    void MarkTemporarilyUnavailable(
        string capabilityKey,
        TimeSpan duration,
        string reason);

    void InvalidateRuntime(
        string reason);

    UniversalCapabilityCacheStatus GetStatus();
}

public sealed class UniversalCapabilityCache
    : IUniversalCapabilityCache
{
    private sealed record CachedRuntime(
        UniversalCapabilitySnapshot Snapshot,
        long Generation,
        DateTimeOffset ExpiresAtUtc);

    private sealed record UnavailableOverride(
        DateTimeOffset ExpiresAtUtc,
        string Reason);

    private readonly object sync = new();
    private readonly ConcurrentDictionary<string, UnavailableOverride>
        unavailable = new(StringComparer.OrdinalIgnoreCase);

    private CachedRuntime? runtime;
    private long generation;
    private long hits;
    private long misses;
    private string lastInvalidationReason =
        "Chưa có invalidation.";

    public bool TryGetRuntimeSnapshot(
        out UniversalCapabilitySnapshot? snapshot)
    {
        lock (sync)
        {
            if (runtime is not null &&
                runtime.Generation == generation &&
                runtime.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                hits++;
                snapshot = ApplyHealthOverrides(
                    runtime.Snapshot);
                return true;
            }

            misses++;
            runtime = null;
            snapshot = null;
            return false;
        }
    }

    public void SetRuntimeSnapshot(
        UniversalCapabilitySnapshot snapshot,
        TimeSpan timeToLive)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (timeToLive <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(timeToLive),
                "TTL capability cache phải lớn hơn 0.");

        lock (sync)
        {
            runtime = new(
                snapshot,
                generation,
                DateTimeOffset.UtcNow +
                    timeToLive);
        }
    }

    public UniversalCapabilitySnapshot ApplyHealthOverrides(
        UniversalCapabilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        RemoveExpiredOverrides();

        var signals =
            snapshot.Signals
                .Select(signal =>
                {
                    if (!unavailable.TryGetValue(
                            signal.Key,
                            out var health))
                    {
                        return signal;
                    }

                    return signal with
                    {
                        Available = false,
                        ConfidenceBonus = 0,
                        Reason =
                            $"Tạm ngừng capability do lỗi kỹ thuật gần đây: {health.Reason}"
                    };
                })
                .ToArray();

        return snapshot with
        {
            Signals = signals
        };
    }

    public void MarkTemporarilyUnavailable(
        string capabilityKey,
        TimeSpan duration,
        string reason)
    {
        var key =
            (capabilityKey ?? string.Empty)
                .Trim();

        if (key.Length == 0)
            throw new ArgumentException(
                "Capability key không được rỗng.",
                nameof(capabilityKey));

        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(duration));

        unavailable[key] = new(
            DateTimeOffset.UtcNow + duration,
            string.IsNullOrWhiteSpace(reason)
                ? "Lỗi kỹ thuật chưa xác định."
                : reason.Trim());

        InvalidateRuntime(
            $"Capability {key} tạm không khả dụng.");
    }

    public void InvalidateRuntime(
        string reason)
    {
        lock (sync)
        {
            generation++;
            runtime = null;
            lastInvalidationReason =
                string.IsNullOrWhiteSpace(reason)
                    ? "Runtime capability cache đã bị vô hiệu hóa."
                    : reason.Trim();
        }
    }

    public UniversalCapabilityCacheStatus GetStatus()
    {
        RemoveExpiredOverrides();

        lock (sync)
        {
            return new(
                generation,
                hits,
                misses,
                RuntimeSnapshotCached:
                    runtime is not null &&
                    runtime.Generation ==
                        generation &&
                    runtime.ExpiresAtUtc >
                        DateTimeOffset.UtcNow,
                runtime?.ExpiresAtUtc,
                unavailable.Count,
                lastInvalidationReason);
        }
    }

    private void RemoveExpiredOverrides()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var pair in unavailable)
        {
            if (pair.Value.ExpiresAtUtc <= now)
            {
                _ = unavailable.TryRemove(
                    pair.Key,
                    out _);
            }
        }
    }
}

public interface IRawUniversalCapabilityDiscoveryService
    : IUniversalCapabilityDiscoveryService
{
}

public sealed class CachedUniversalCapabilityDiscoveryService(
    IRawUniversalCapabilityDiscoveryService raw,
    IUniversalCapabilityCache cache)
    : IUniversalCapabilityDiscoveryService
{
    private static readonly TimeSpan RuntimeTtl =
        TimeSpan.FromSeconds(5);

    public UniversalCapabilitySnapshot Discover()
    {
        if (cache.TryGetRuntimeSnapshot(
                out var cached) &&
            cached is not null)
        {
            return cached;
        }

        var discovered =
            raw.Discover();

        var healthy =
            cache.ApplyHealthOverrides(
                discovered);

        cache.SetRuntimeSnapshot(
            healthy,
            RuntimeTtl);

        return healthy;
    }
}
