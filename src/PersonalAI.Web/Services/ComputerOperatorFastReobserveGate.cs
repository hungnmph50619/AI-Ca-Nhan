namespace PersonalAI.Web.Services;

public sealed record FastReobserveResult(
    bool EventDriven,
    bool EventReceived,
    string EventKind,
    TimeSpan Waited,
    TimeSpan MaximumWait,
    string Reason);

public interface IComputerOperatorFastReobserveGate
{
    Task<FastReobserveResult> WaitBeforeVerificationAsync(
        string? action,
        CancellationToken cancellationToken = default);
}

public sealed class ComputerOperatorFastReobserveGate(
    IAdaptiveObservationWakeSource wakeSource)
    : IComputerOperatorFastReobserveGate
{
    public async Task<FastReobserveResult> WaitBeforeVerificationAsync(
        string? action,
        CancellationToken cancellationToken = default)
    {
        var normalized =
            (action ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        // Cursor position can be read deterministically. It does not need a
        // window event, only a tiny scheduler/input-settle allowance.
        if (normalized == "move-pointer")
        {
            var delay =
                TimeSpan.FromMilliseconds(40);

            var started =
                DateTimeOffset.UtcNow;

            await Task.Delay(
                delay,
                cancellationToken);

            return new(
                EventDriven: false,
                EventReceived: false,
                EventKind: string.Empty,
                DateTimeOffset.UtcNow - started,
                delay,
                "Move-pointer dùng local cursor readback; chỉ chờ 40ms trước khi đọc lại.");
        }

        var maximumWait =
            MaximumWaitForAction(
                normalized);

        var wake =
            await wakeSource.WaitAsync(
                maximumWait,
                cancellationToken);

        return new(
            wakeSource.EventDrivenAvailable,
            wake.EventReceived,
            wake.Event?.Kind ?? string.Empty,
            wake.Waited,
            maximumWait,
            wake.EventReceived
                ? $"Được đánh thức sớm bởi desktop event: {wake.Reason}"
                : $"Không có desktop event trước {maximumWait.TotalMilliseconds:0}ms; dùng timeout fallback rồi quan sát.");
    }

    internal static TimeSpan MaximumWaitForAction(
        string action) =>
        action switch
        {
            "focus-window" or
            "maximize" or
            "restore" or
            "minimize" =>
                TimeSpan.FromMilliseconds(120),

            "click-left" or
            "double-click-left" or
            "click-right" or
            "press-key" or
            "press-hotkey" or
            "scroll" or
            "drag-left" =>
                TimeSpan.FromMilliseconds(180),

            _ =>
                TimeSpan.FromMilliseconds(220)
        };
}
