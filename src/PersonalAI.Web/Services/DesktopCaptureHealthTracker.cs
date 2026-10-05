using System.Collections.Concurrent;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record DesktopCaptureHealthEntry(
    string Scope,
    string Target,
    string Backend,
    long SuccessCount,
    long FailureCount,
    long FallbackCount,
    double LastLatencyMs,
    double AverageLatencyMs,
    int ConsecutiveUnchangedFrames,
    bool StaleSuspected,
    DateTimeOffset LastUpdatedUtc,
    string? LastFallbackReason);

public sealed record DesktopCaptureHealthSnapshot(
    string Version,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<DesktopCaptureHealthEntry> Entries);

public interface IDesktopCaptureHealthTracker
{
    void RecordSuccess(
        string scope,
        string target,
        string backend,
        long latencyMs,
        ReadOnlySpan<byte> signature,
        string? fallbackReason = null);

    void RecordFailure(
        string scope,
        string target,
        string backend,
        long latencyMs,
        string? detail = null);

    DesktopCaptureHealthSnapshot GetSnapshot();

    void Reset();
}

public sealed class DesktopCaptureHealthTracker
    : IDesktopCaptureHealthTracker
{
    private readonly ConcurrentDictionary<string, MutableEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public void RecordSuccess(
        string scope,
        string target,
        string backend,
        long latencyMs,
        ReadOnlySpan<byte> signature,
        string? fallbackReason = null)
    {
        var normalizedScope = Normalize(scope);
        var normalizedTarget = NormalizeTarget(target);
        var normalizedBackend = Normalize(backend);
        var key = BuildKey(
            normalizedScope,
            normalizedTarget,
            normalizedBackend);
        var fingerprint = ComputeFingerprint(
            signature);

        var entry = _entries.GetOrAdd(
            key,
            _ => new MutableEntry(
                normalizedScope,
                normalizedTarget,
                normalizedBackend));

        lock (entry.Sync)
        {
            entry.SuccessCount++;
            entry.TotalLatencyMs += Math.Max(
                0,
                latencyMs);
            entry.LastLatencyMs = Math.Max(
                0,
                latencyMs);

            if (!string.IsNullOrWhiteSpace(
                    fallbackReason))
            {
                entry.FallbackCount++;
                entry.LastFallbackReason =
                    fallbackReason.Trim();
            }

            if (fingerprint != 0 &&
                fingerprint == entry.LastFingerprint)
            {
                entry.ConsecutiveUnchangedFrames++;
            }
            else
            {
                entry.ConsecutiveUnchangedFrames = 0;
            }

            entry.LastFingerprint =
                fingerprint;

            entry.LastUpdatedUtc =
                DateTimeOffset.UtcNow;
        }
    }

    public void RecordFailure(
        string scope,
        string target,
        string backend,
        long latencyMs,
        string? detail = null)
    {
        var normalizedScope = Normalize(scope);
        var normalizedTarget = NormalizeTarget(target);
        var normalizedBackend = Normalize(backend);
        var key = BuildKey(
            normalizedScope,
            normalizedTarget,
            normalizedBackend);

        var entry = _entries.GetOrAdd(
            key,
            _ => new MutableEntry(
                normalizedScope,
                normalizedTarget,
                normalizedBackend));

        lock (entry.Sync)
        {
            entry.FailureCount++;
            entry.LastLatencyMs = Math.Max(
                0,
                latencyMs);
            entry.LastFallbackReason =
                string.IsNullOrWhiteSpace(detail)
                    ? entry.LastFallbackReason
                    : detail.Trim();
            entry.LastUpdatedUtc =
                DateTimeOffset.UtcNow;
        }
    }

    public DesktopCaptureHealthSnapshot GetSnapshot()
    {
        var entries =
            _entries.Values
                .Select(ToSnapshot)
                .OrderBy(item => item.Scope)
                .ThenBy(item => item.Target)
                .ThenBy(item => item.Backend)
                .ToArray();

        return new(
            PersonalAiRelease.Version,
            DateTimeOffset.UtcNow,
            entries);
    }

    public void Reset() =>
        _entries.Clear();

    private static DesktopCaptureHealthEntry ToSnapshot(
        MutableEntry entry)
    {
        lock (entry.Sync)
        {
            var average =
                entry.SuccessCount == 0
                    ? 0
                    : entry.TotalLatencyMs /
                      (double)entry.SuccessCount;

            return new(
                entry.Scope,
                entry.Target,
                entry.Backend,
                entry.SuccessCount,
                entry.FailureCount,
                entry.FallbackCount,
                entry.LastLatencyMs,
                average,
                entry.ConsecutiveUnchangedFrames,
                entry.ConsecutiveUnchangedFrames >= 4,
                entry.LastUpdatedUtc,
                entry.LastFallbackReason);
        }
    }

    private static string BuildKey(
        string scope,
        string target,
        string backend) =>
        string.Join(
            "|",
            scope,
            target,
            backend);

    private static string Normalize(
        string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim().ToLowerInvariant();

    private static string NormalizeTarget(
        string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Trim();

    private static ulong ComputeFingerprint(
        ReadOnlySpan<byte> signature)
    {
        if (signature.IsEmpty)
            return 0;

        const ulong offset = 1469598103934665603;
        const ulong prime = 1099511628211;
        var hash = offset;

        foreach (var value in signature)
        {
            hash ^= value;
            hash *= prime;
        }

        return hash;
    }

    private sealed class MutableEntry(
        string scope,
        string target,
        string backend)
    {
        public object Sync { get; } = new();
        public string Scope { get; } = scope;
        public string Target { get; } = target;
        public string Backend { get; } = backend;
        public long SuccessCount { get; set; }
        public long FailureCount { get; set; }
        public long FallbackCount { get; set; }
        public long TotalLatencyMs { get; set; }
        public double LastLatencyMs { get; set; }
        public int ConsecutiveUnchangedFrames { get; set; }
        public ulong LastFingerprint { get; set; }
        public DateTimeOffset LastUpdatedUtc { get; set; }
        public string? LastFallbackReason { get; set; }
    }
}
