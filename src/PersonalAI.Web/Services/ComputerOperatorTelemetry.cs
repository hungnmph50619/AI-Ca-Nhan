using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class ComputerOperatorTelemetryStages
{
    public const string Task = "task";
    public const string Observe = "observe";
    public const string GeminiPlan = "gemini-plan";
    public const string Execute = "execute";
    public const string Verify = "verify";
    public const string GeminiVerify = "gemini-verify";
    public const string AdaptiveWait = "adaptive-wait";
    public const string FastReobserve = "fast-reobserve";
    public const string ReplanPacing = "replan-pacing";
    public const string TextEngine = "text-engine";
}

public sealed record ComputerOperatorTelemetryEvent(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string TraceId,
    string Stage,
    string Action,
    string Route,
    bool Success,
    long DurationMilliseconds);

public sealed record ComputerOperatorTelemetryAggregate(
    string Stage,
    long Count,
    long SuccessCount,
    long FailureCount,
    double AverageMilliseconds,
    long MaximumMilliseconds);

public sealed record ComputerOperatorTelemetrySnapshot(
    string Version,
    bool MemoryOnly,
    bool OpenTelemetryCompatible,
    bool ContainsGoals,
    bool ContainsTextPayloads,
    bool ContainsScreenshots,
    bool ContainsCoordinates,
    int MaximumRecentEvents,
    IReadOnlyList<ComputerOperatorTelemetryAggregate> Aggregates,
    IReadOnlyList<ComputerOperatorTelemetryEvent> RecentEvents);

public interface IComputerOperatorTelemetryOperation : IDisposable
{
    void Complete(
        bool success = true,
        string? route = null);
}

public interface IComputerOperatorTelemetry
{
    IComputerOperatorTelemetryOperation Begin(
        string stage,
        string? action = null);

    ComputerOperatorTelemetrySnapshot GetSnapshot();

    void Reset();
}

public sealed class ComputerOperatorTelemetry
    : IComputerOperatorTelemetry
{
    public const int MaximumRecentEvents = 200;

    private static readonly Regex SafeDimension = new(
        "^[a-z0-9][a-z0-9._-]{0,39}$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    private static readonly ActivitySource ActivitySource =
        new("PersonalAI.ComputerOperator");

    private static readonly Meter Meter =
        new("PersonalAI.ComputerOperator", "1.0");

    private static readonly Histogram<double> DurationHistogram =
        Meter.CreateHistogram<double>(
            "personalai.computer_operator.stage.duration",
            unit: "ms",
            description:
                "Thời gian từng giai đoạn Computer Operator.");

    private static readonly Counter<long> OperationCounter =
        Meter.CreateCounter<long>(
            "personalai.computer_operator.stage.count",
            unit: "{operation}",
            description:
                "Số lần chạy từng giai đoạn Computer Operator.");

    private readonly object gate = new();
    private readonly Queue<ComputerOperatorTelemetryEvent> recent = new();
    private readonly Dictionary<string, MutableAggregate> aggregates =
        new(StringComparer.OrdinalIgnoreCase);
    private long sequence;

    public IComputerOperatorTelemetryOperation Begin(
        string stage,
        string? action = null)
    {
        var normalizedStage =
            NormalizeDimension(stage);

        var normalizedAction =
            NormalizeDimension(action);

        return new Operation(
            this,
            normalizedStage,
            normalizedAction);
    }

    public ComputerOperatorTelemetrySnapshot GetSnapshot()
    {
        lock (gate)
        {
            return new(
                PersonalAiRelease.Version,
                MemoryOnly: true,
                OpenTelemetryCompatible: true,
                ContainsGoals: false,
                ContainsTextPayloads: false,
                ContainsScreenshots: false,
                ContainsCoordinates: false,
                MaximumRecentEvents,
                aggregates
                    .OrderBy(pair =>
                        pair.Key,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(pair =>
                        pair.Value.ToPublic(
                            pair.Key))
                    .ToArray(),
                recent.ToArray());
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            recent.Clear();
            aggregates.Clear();
        }
    }

    private void Record(
        string stage,
        string action,
        string route,
        bool success,
        TimeSpan elapsed,
        Activity? activity)
    {
        var milliseconds =
            Math.Max(
                0,
                (long)Math.Round(
                    elapsed.TotalMilliseconds));

        var tags =
            new TagList
            {
                { "stage", stage },
                { "action", action },
                { "route", route },
                { "success", success }
            };

        DurationHistogram.Record(
            elapsed.TotalMilliseconds,
            tags);
        OperationCounter.Add(
            1,
            tags);

        if (activity is not null)
        {
            activity.SetTag(
                "personalai.stage",
                stage);
            activity.SetTag(
                "personalai.action",
                action);
            activity.SetTag(
                "personalai.route",
                route);
            activity.SetStatus(
                success
                    ? ActivityStatusCode.Ok
                    : ActivityStatusCode.Error);
        }

        var traceId =
            activity?.TraceId.ToString();

        if (string.IsNullOrWhiteSpace(traceId))
        {
            traceId =
                Activity.Current?.TraceId.ToString();
        }

        if (string.IsNullOrWhiteSpace(traceId))
        {
            traceId =
                Guid.NewGuid()
                    .ToString("N");
        }

        lock (gate)
        {
            sequence++;

            recent.Enqueue(
                new(
                    sequence,
                    DateTimeOffset.UtcNow,
                    traceId,
                    stage,
                    action,
                    route,
                    success,
                    milliseconds));

            while (recent.Count >
                   MaximumRecentEvents)
            {
                recent.Dequeue();
            }

            if (!aggregates.TryGetValue(
                    stage,
                    out var aggregate))
            {
                aggregate = new();
                aggregates[stage] =
                    aggregate;
            }

            aggregate.Count++;
            if (success)
                aggregate.SuccessCount++;
            else
                aggregate.FailureCount++;

            aggregate.TotalMilliseconds +=
                milliseconds;
            aggregate.MaximumMilliseconds =
                Math.Max(
                    aggregate.MaximumMilliseconds,
                    milliseconds);
        }
    }

    private static string NormalizeDimension(
        string? value)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        if (normalized.Length == 0)
            return "none";

        return SafeDimension.IsMatch(normalized)
            ? normalized
            : "other";
    }

    private sealed class Operation
        : IComputerOperatorTelemetryOperation
    {
        private readonly ComputerOperatorTelemetry owner;
        private readonly string stage;
        private readonly string action;
        private readonly Stopwatch stopwatch =
            Stopwatch.StartNew();
        private readonly Activity? activity;
        private int completed;

        public Operation(
            ComputerOperatorTelemetry owner,
            string stage,
            string action)
        {
            this.owner = owner;
            this.stage = stage;
            this.action = action;

            activity =
                ActivitySource.StartActivity(
                    stage,
                    ActivityKind.Internal);

            activity?.SetTag(
                "personalai.stage",
                stage);
            activity?.SetTag(
                "personalai.action",
                action);
        }

        public void Complete(
            bool success = true,
            string? route = null)
        {
            if (Interlocked.Exchange(
                    ref completed,
                    1) != 0)
            {
                return;
            }

            stopwatch.Stop();

            var normalizedRoute =
                NormalizeDimension(route);

            owner.Record(
                stage,
                action,
                normalizedRoute,
                success,
                stopwatch.Elapsed,
                activity);

            activity?.Dispose();
        }

        public void Dispose()
        {
            if (Volatile.Read(
                    ref completed) != 0)
            {
                return;
            }

            Complete(
                success: false,
                route: "incomplete");
        }
    }

    private sealed class MutableAggregate
    {
        public long Count { get; set; }
        public long SuccessCount { get; set; }
        public long FailureCount { get; set; }
        public long TotalMilliseconds { get; set; }
        public long MaximumMilliseconds { get; set; }

        public ComputerOperatorTelemetryAggregate ToPublic(
            string stage) =>
            new(
                stage,
                Count,
                SuccessCount,
                FailureCount,
                Count == 0
                    ? 0
                    : (double)TotalMilliseconds /
                      Count,
                MaximumMilliseconds);
    }
}
