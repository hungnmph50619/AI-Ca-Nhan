namespace PersonalAI.Web.Services;

public sealed record ReplanPacingResult(
    bool EventDriven,
    bool EventReceived,
    string EventKind,
    TimeSpan Waited,
    TimeSpan MaximumWait,
    string Reason);

public interface IComputerOperatorReplanPacer
{
    Task<ReplanPacingResult> WaitAsync(
        TimeSpan maximumWait,
        string reason,
        CancellationToken cancellationToken = default);
}

public sealed class ComputerOperatorReplanPacer(
    IAdaptiveObservationWakeSource wakeSource)
    : IComputerOperatorReplanPacer
{
    private static readonly TimeSpan MinimumFloor =
        TimeSpan.FromMilliseconds(80);

    public async Task<ReplanPacingResult> WaitAsync(
        TimeSpan maximumWait,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (maximumWait <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(maximumWait));

        var started =
            DateTimeOffset.UtcNow;

        var floor =
            maximumWait < MinimumFloor
                ? maximumWait
                : MinimumFloor;

        await Task.Delay(
            floor,
            cancellationToken);

        var remaining =
            maximumWait - floor;

        if (remaining <= TimeSpan.Zero)
        {
            return new(
                EventDriven: false,
                EventReceived: false,
                EventKind: string.Empty,
                DateTimeOffset.UtcNow - started,
                maximumWait,
                $"{reason} Đã giữ minimum pacing {floor.TotalMilliseconds:0}ms.");
        }

        var wake =
            await wakeSource.WaitAsync(
                remaining,
                cancellationToken);

        return new(
            wakeSource.EventDrivenAvailable,
            wake.EventReceived,
            wake.Event?.Kind ?? string.Empty,
            DateTimeOffset.UtcNow - started,
            maximumWait,
            wake.EventReceived
                ? $"{reason} Được đánh thức sớm bởi desktop event: {wake.Reason}"
                : $"{reason} Không có event; đã giữ đủ fallback pacing {maximumWait.TotalMilliseconds:0}ms.");
    }
}
