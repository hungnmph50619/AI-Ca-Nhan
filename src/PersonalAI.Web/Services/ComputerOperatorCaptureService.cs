using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerOperatorCaptureService
{
    Task<DesktopScreenshotFrame> CaptureObservationAsync(
        CancellationToken cancellationToken = default);

    Task<DesktopScreenshotFrame> CaptureAsync(
        VerificationCaptureContext? context,
        CancellationToken cancellationToken = default);

    Task<DesktopScreenshotFrame> CaptureDynamicAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Chọn capture source ổn định cho observation/verification.
/// Chỉ thu thập evidence; không plan, verify outcome hay execute action.
/// </summary>
public sealed class ComputerOperatorCaptureService(
    IComputerUseService computer,
    IDesktopScreenshotService screenshots,
    IComputerDisplayTopologyService displays,
    ILogger<ComputerOperatorCaptureService> logger)
    : IComputerOperatorCaptureService
{
    private const int MaximumWaitMs = 5000;

    public Task<DesktopScreenshotFrame> CaptureObservationAsync(
        CancellationToken cancellationToken = default) =>
        screenshots.CaptureStableVirtualScreenAsync(
            MaximumWaitMs,
            cancellationToken);

    public async Task<DesktopScreenshotFrame> CaptureAsync(
        VerificationCaptureContext? context,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
            return await CaptureDynamicAsync(cancellationToken);

        try
        {
            if (context.CaptureScope.Equals(
                    "window",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(context.WindowId))
            {
                return await screenshots.CaptureStableWindowAsync(
                    context.WindowId,
                    MaximumWaitMs,
                    cancellationToken);
            }

            if (context.CaptureScope.Equals(
                    "monitor",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(context.MonitorDevice))
            {
                return await screenshots.CaptureStableMonitorAsync(
                    context.MonitorDevice,
                    MaximumWaitMs,
                    cancellationToken);
            }

            if (context.CaptureScope.Equals(
                    "virtual-desktop",
                    StringComparison.OrdinalIgnoreCase))
            {
                return await screenshots.CaptureStableVirtualScreenAsync(
                    MaximumWaitMs,
                    cancellationToken);
            }
        }
        catch (Exception exception) when (
            exception is ToolExecutionInputException or
            InvalidOperationException)
        {
            logger.LogDebug(
                exception,
                "Không giữ được Verification Capture Context; fallback về capture động.");
        }

        return await CaptureDynamicAsync(cancellationToken);
    }

    public async Task<DesktopScreenshotFrame> CaptureDynamicAsync(
        CancellationToken cancellationToken = default)
    {
        var active = computer.GetActiveWindow();

        if (active is not null &&
            active.Width >= 64 &&
            active.Height >= 64)
        {
            try
            {
                return await screenshots.CaptureStableWindowAsync(
                    active.WindowId,
                    MaximumWaitMs,
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is ToolExecutionInputException or
                InvalidOperationException)
            {
                logger.LogDebug(
                    exception,
                    "Không chụp ổn định được foreground window {WindowId}; thử monitor theo con trỏ.",
                    active.WindowId);
            }
        }

        try
        {
            var cursor = computer.GetCursorPosition();
            var monitor = displays.GetMonitorAtPoint(
                cursor.X,
                cursor.Y);

            if (monitor is not null)
            {
                return await screenshots.CaptureStableMonitorAsync(
                    monitor.DeviceName,
                    MaximumWaitMs,
                    cancellationToken);
            }
        }
        catch (Exception exception) when (
            exception is ToolExecutionInputException or
            InvalidOperationException)
        {
            logger.LogDebug(
                exception,
                "Không chụp ổn định được monitor theo con trỏ; fallback về virtual desktop.");
        }

        return await screenshots.CaptureStableVirtualScreenAsync(
            MaximumWaitMs,
            cancellationToken);
    }
}
