namespace PersonalAI.Web.Services;

public static class ComputerOperatorPerformanceStatuses
{
    public const string Pass = "pass";
    public const string Watch = "watch";
    public const string NoData = "no-data";
    public const string Informational = "informational";
}

public sealed record ComputerOperatorStageSlo(
    string Stage,
    string Status,
    long Count,
    double AverageMilliseconds,
    long MaximumMilliseconds,
    double? TargetAverageMilliseconds,
    string Reason);

public sealed record ComputerOperatorPerformanceReport(
    string Version,
    IReadOnlyList<ComputerOperatorStageSlo> Stages,
    long GeminiPlanningCalls,
    long GeminiVerificationCalls,
    long LocalVerificationEvents,
    long SemanticVerificationEvents,
    long EventWakeups,
    long TimeoutWakeups,
    double EventWakeRatio,
    string Summary);

public interface IComputerOperatorPerformanceProfileService
{
    ComputerOperatorPerformanceReport GetReport();
}

public sealed class ComputerOperatorPerformanceProfileService(
    IComputerOperatorTelemetry telemetry)
    : IComputerOperatorPerformanceProfileService
{
    public ComputerOperatorPerformanceReport GetReport() =>
        Evaluate(
            telemetry.GetSnapshot());

    internal static ComputerOperatorPerformanceReport Evaluate(
        ComputerOperatorTelemetrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var targets =
            new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase)
            {
                [ComputerOperatorTelemetryStages.FastReobserve] = 250,
                [ComputerOperatorTelemetryStages.TextEngine] = 1000,
                [ComputerOperatorTelemetryStages.Execute] = 1000,
                [ComputerOperatorTelemetryStages.Observe] = 1200
            };

        var stages =
            snapshot.Aggregates
                .OrderBy(item =>
                    item.Stage,
                    StringComparer.OrdinalIgnoreCase)
                .Select(item =>
                {
                    if (targets.TryGetValue(
                            item.Stage,
                            out var target))
                    {
                        var status =
                            item.AverageMilliseconds <= target
                                ? ComputerOperatorPerformanceStatuses.Pass
                                : ComputerOperatorPerformanceStatuses.Watch;

                        return new ComputerOperatorStageSlo(
                            item.Stage,
                            status,
                            item.Count,
                            item.AverageMilliseconds,
                            item.MaximumMilliseconds,
                            target,
                            status ==
                                ComputerOperatorPerformanceStatuses.Pass
                                ? $"Average {item.AverageMilliseconds:0}ms đạt mục tiêu <= {target:0}ms."
                                : $"Average {item.AverageMilliseconds:0}ms vượt mục tiêu {target:0}ms; nên kiểm tra telemetry chi tiết trước khi tối ưu tiếp.");
                    }

                    return new ComputerOperatorStageSlo(
                        item.Stage,
                        ComputerOperatorPerformanceStatuses.Informational,
                        item.Count,
                        item.AverageMilliseconds,
                        item.MaximumMilliseconds,
                        TargetAverageMilliseconds: null,
                        "Stage này mang tính semantic/network hoặc phụ thuộc workload; chỉ theo dõi, không hard-fail.");
                })
                .ToList();

        foreach (var target in targets)
        {
            if (stages.Any(item =>
                    item.Stage.Equals(
                        target.Key,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            stages.Add(
                new(
                    target.Key,
                    ComputerOperatorPerformanceStatuses.NoData,
                    Count: 0,
                    AverageMilliseconds: 0,
                    MaximumMilliseconds: 0,
                    target.Value,
                    "Chưa có dữ liệu đủ để đánh giá."));
        }

        stages =
            stages
                .OrderBy(item =>
                    item.Stage,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        var recent =
            snapshot.RecentEvents;

        var eventWakeups =
            recent.LongCount(item =>
                (item.Stage.Equals(
                     ComputerOperatorTelemetryStages.FastReobserve,
                     StringComparison.OrdinalIgnoreCase) ||
                 item.Stage.Equals(
                     ComputerOperatorTelemetryStages.ReplanPacing,
                     StringComparison.OrdinalIgnoreCase)) &&
                item.Route.Equals(
                    "event",
                    StringComparison.OrdinalIgnoreCase));

        var timeoutWakeups =
            recent.LongCount(item =>
                (item.Stage.Equals(
                     ComputerOperatorTelemetryStages.FastReobserve,
                     StringComparison.OrdinalIgnoreCase) ||
                 item.Stage.Equals(
                     ComputerOperatorTelemetryStages.ReplanPacing,
                     StringComparison.OrdinalIgnoreCase)) &&
                item.Route.Equals(
                    "timeout",
                    StringComparison.OrdinalIgnoreCase));

        var wakeTotal =
            eventWakeups +
            timeoutWakeups;

        var eventWakeRatio =
            wakeTotal == 0
                ? 0
                : eventWakeups /
                  (double)wakeTotal;

        var planningCalls =
            snapshot.Aggregates
                .Where(item =>
                    item.Stage.Equals(
                        ComputerOperatorTelemetryStages.GeminiPlan,
                        StringComparison.OrdinalIgnoreCase))
                .Sum(item => item.Count);

        var verificationCalls =
            snapshot.Aggregates
                .Where(item =>
                    item.Stage.Equals(
                        ComputerOperatorTelemetryStages.GeminiVerify,
                        StringComparison.OrdinalIgnoreCase))
                .Sum(item => item.Count);

        var localVerificationEvents =
            recent.LongCount(item =>
                item.Stage.Equals(
                    ComputerOperatorTelemetryStages.Verify,
                    StringComparison.OrdinalIgnoreCase) &&
                item.Route.Equals(
                    "local",
                    StringComparison.OrdinalIgnoreCase));

        var semanticVerificationEvents =
            recent.LongCount(item =>
                item.Stage.Equals(
                    ComputerOperatorTelemetryStages.Verify,
                    StringComparison.OrdinalIgnoreCase) &&
                item.Route.Equals(
                    "semantic",
                    StringComparison.OrdinalIgnoreCase));

        var watchCount =
            stages.Count(item =>
                item.Status ==
                ComputerOperatorPerformanceStatuses.Watch);

        var summary =
            watchCount == 0
                ? "Chưa có stage local nào vượt SLO quan sát. Tiếp tục ưu tiên accuracy và chỉ tối ưu khi telemetry cho thấy bottleneck thật."
                : $"Có {watchCount} stage local vượt SLO quan sát; nên tối ưu đúng các stage này thay vì giảm verification toàn cục.";

        return new(
            snapshot.Version,
            stages,
            planningCalls,
            verificationCalls,
            localVerificationEvents,
            semanticVerificationEvents,
            eventWakeups,
            timeoutWakeups,
            eventWakeRatio,
            summary);
    }
}
