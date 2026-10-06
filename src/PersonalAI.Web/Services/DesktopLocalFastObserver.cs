using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record DesktopFastObserverSample(
    DateTimeOffset CapturedAtUtc,
    string CaptureScope,
    string? WindowId,
    string? ActiveWindowId,
    string? ActiveProcessName,
    string? ActiveWindowTitle,
    int WindowLeft,
    int WindowTop,
    int WindowWidth,
    int WindowHeight,
    int? CursorX,
    int? CursorY,
    string? MonitorDevice,
    uint DpiX,
    uint DpiY,
    bool WindowLikelyOccluded);

public sealed record DesktopFastObservation(
    bool ScreenChanged,
    double ChangeRatio,
    bool ForegroundWindowChanged,
    bool WindowBoundsChanged,
    bool CursorMoved,
    bool MonitorChanged,
    bool DpiChanged,
    bool TargetMoved,
    bool TargetMissing,
    bool TargetLikelyOccluded,
    string Summary);

public interface IDesktopLocalFastObserver
{
    DesktopFastObserverSample CaptureSample(DesktopScreenshotFrame frame);

    DesktopFastObservation Analyze(
        DesktopFastObserverSample before,
        DesktopFastObserverSample after,
        DesktopFrameDifference? frameDifference = null,
        DesktopSceneElement? targetBefore = null,
        DesktopSceneElement? targetAfter = null);
}

public sealed class DesktopLocalFastObserver(
    IComputerUseService computer,
    IComputerDisplayTopologyService displays)
    : IDesktopLocalFastObserver
{
    private const int TargetMovementThresholdPixels = 12;

    public DesktopFastObserverSample CaptureSample(
        DesktopScreenshotFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var active = computer.GetActiveWindow();

        int? cursorX = null;
        int? cursorY = null;
        string? monitorDevice = frame.MonitorDevice;
        uint dpiX = frame.MonitorDpiX;
        uint dpiY = frame.MonitorDpiY;

        try
        {
            var cursor = computer.GetCursorPosition();
            cursorX = cursor.X;
            cursorY = cursor.Y;

            var monitor = displays.GetMonitorAtPoint(
                cursor.X,
                cursor.Y);

            if (monitor is not null)
            {
                monitorDevice ??= monitor.DeviceName;
                dpiX = monitor.DpiX;
                dpiY = monitor.DpiY;
            }
        }
        catch (Exception exception) when (
            exception is ToolExecutionInputException or
            InvalidOperationException or
            NotSupportedException)
        {
            // Cursor/monitor metadata is optional for the fast observer.
        }

        return new(
            frame.CapturedAtUtc,
            frame.CaptureScope,
            frame.WindowId,
            active?.WindowId,
            active?.ProcessName,
            active?.Title,
            active?.Left ?? frame.Left,
            active?.Top ?? frame.Top,
            active?.Width ?? frame.Width,
            active?.Height ?? frame.Height,
            cursorX,
            cursorY,
            monitorDevice,
            dpiX,
            dpiY,
            frame.WindowLikelyOccluded);
    }

    public DesktopFastObservation Analyze(
        DesktopFastObserverSample before,
        DesktopFastObserverSample after,
        DesktopFrameDifference? frameDifference = null,
        DesktopSceneElement? targetBefore = null,
        DesktopSceneElement? targetAfter = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var screenChanged =
            frameDifference?.Comparable == true &&
            frameDifference.HasMeaningfulChange;

        var foregroundChanged =
            !string.Equals(
                before.ActiveWindowId,
                after.ActiveWindowId,
                StringComparison.OrdinalIgnoreCase);

        var boundsChanged =
            before.WindowLeft != after.WindowLeft ||
            before.WindowTop != after.WindowTop ||
            before.WindowWidth != after.WindowWidth ||
            before.WindowHeight != after.WindowHeight;

        var cursorMoved =
            before.CursorX.HasValue &&
            before.CursorY.HasValue &&
            after.CursorX.HasValue &&
            after.CursorY.HasValue &&
            (before.CursorX.Value != after.CursorX.Value ||
             before.CursorY.Value != after.CursorY.Value);

        var monitorChanged =
            !string.Equals(
                before.MonitorDevice,
                after.MonitorDevice,
                StringComparison.OrdinalIgnoreCase);

        var dpiChanged =
            before.DpiX != after.DpiX ||
            before.DpiY != after.DpiY;

        var targetMissing =
            targetBefore is not null &&
            targetAfter is null;

        var targetMoved = false;
        if (targetBefore is not null &&
            targetAfter is not null)
        {
            var beforeCenterX =
                targetBefore.BoxLeft + targetBefore.BoxWidth / 2;
            var beforeCenterY =
                targetBefore.BoxTop + targetBefore.BoxHeight / 2;
            var afterCenterX =
                targetAfter.BoxLeft + targetAfter.BoxWidth / 2;
            var afterCenterY =
                targetAfter.BoxTop + targetAfter.BoxHeight / 2;

            targetMoved =
                Math.Abs(afterCenterX - beforeCenterX) >
                    TargetMovementThresholdPixels ||
                Math.Abs(afterCenterY - beforeCenterY) >
                    TargetMovementThresholdPixels;
        }

        var targetLikelyOccluded =
            after.WindowLikelyOccluded ||
            targetMissing;

        var ratio = frameDifference?.Comparable == true
            ? frameDifference.ChangedRatio
            : 0;

        var summary =
            $"screenChanged={screenChanged}; changeRatio={ratio:0.0000}; " +
            $"foregroundChanged={foregroundChanged}; boundsChanged={boundsChanged}; " +
            $"cursorMoved={cursorMoved}; monitorChanged={monitorChanged}; " +
            $"dpiChanged={dpiChanged}; targetMoved={targetMoved}; " +
            $"targetMissing={targetMissing}; occluded={targetLikelyOccluded}";

        return new(
            screenChanged,
            ratio,
            foregroundChanged,
            boundsChanged,
            cursorMoved,
            monitorChanged,
            dpiChanged,
            targetMoved,
            targetMissing,
            targetLikelyOccluded,
            summary);
    }
}
