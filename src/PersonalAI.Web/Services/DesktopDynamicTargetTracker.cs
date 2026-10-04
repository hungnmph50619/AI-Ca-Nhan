using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record DesktopTargetTrackingResult(
    DesktopOperatorDecision Decision,
    bool Adjusted,
    bool SafeToExecute,
    double Confidence,
    string Reason);

public interface IDesktopDynamicTargetTracker
{
    DesktopTargetTrackingResult Track(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame planningFrame,
        ComputerWindowInfo? plannedWindow,
        ComputerWindowInfo? currentWindow);
}

public sealed class DesktopDynamicTargetTracker
    : IDesktopDynamicTargetTracker
{
    private const double MinimumScale = 0.50;
    private const double MaximumScale = 2.00;

    public DesktopTargetTrackingResult Track(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame planningFrame,
        ComputerWindowInfo? plannedWindow,
        ComputerWindowInfo? currentWindow)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(planningFrame);

        var space = string.IsNullOrWhiteSpace(decision.CoordinateSpace)
            ? ComputerCoordinateSpaces.ImagePixel
            : decision.CoordinateSpace.Trim().ToLowerInvariant();

        if (space == ComputerCoordinateSpaces.WindowNormalized)
        {
            return new(
                decision,
                false,
                true,
                0.99,
                "Target đã dùng tọa độ chuẩn hóa theo cửa sổ; không cần remap pixel.");
        }

        if (space != ComputerCoordinateSpaces.ImagePixel ||
            decision.BoxWidth <= 1 ||
            decision.BoxHeight <= 1)
        {
            return new(
                decision,
                false,
                true,
                0.90,
                "Target không dùng bbox pixel cần theo dõi động.");
        }

        if (plannedWindow is null ||
            currentWindow is null)
        {
            return new(
                decision,
                false,
                false,
                0.0,
                "Không đủ metadata cửa sổ để remap target pixel an toàn.");
        }

        if (!plannedWindow.WindowId.Equals(
                currentWindow.WindowId,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                decision,
                false,
                false,
                0.0,
                "Cửa sổ hiện tại không còn trùng với cửa sổ chứa target lúc lập kế hoạch.");
        }

        if (plannedWindow.Width <= 0 ||
            plannedWindow.Height <= 0 ||
            currentWindow.Width <= 0 ||
            currentWindow.Height <= 0)
        {
            return new(
                decision,
                false,
                false,
                0.0,
                "Geometry cửa sổ không hợp lệ để theo dõi target.");
        }

        var scaleX = currentWindow.Width / (double)plannedWindow.Width;
        var scaleY = currentWindow.Height / (double)plannedWindow.Height;

        if (scaleX is < MinimumScale or > MaximumScale ||
            scaleY is < MinimumScale or > MaximumScale)
        {
            return new(
                decision,
                false,
                false,
                0.0,
                $"Cửa sổ resize quá lớn để remap an toàn: scale=({scaleX:0.00},{scaleY:0.00}).");
        }

        var boxDesktopLeft =
            planningFrame.Left + decision.BoxLeft;
        var boxDesktopTop =
            planningFrame.Top + decision.BoxTop;

        var relativeLeft =
            (boxDesktopLeft - plannedWindow.Left) /
            (double)plannedWindow.Width;
        var relativeTop =
            (boxDesktopTop - plannedWindow.Top) /
            (double)plannedWindow.Height;
        var relativeWidth =
            decision.BoxWidth /
            (double)plannedWindow.Width;
        var relativeHeight =
            decision.BoxHeight /
            (double)plannedWindow.Height;

        if (!WithinReasonableWindowRange(
                relativeLeft,
                relativeTop,
                relativeWidth,
                relativeHeight))
        {
            return new(
                decision,
                false,
                false,
                0.0,
                "BBox target không nằm hợp lý trong cửa sổ đã quan sát.");
        }

        var newDesktopLeft =
            currentWindow.Left +
            (int)Math.Round(
                relativeLeft * currentWindow.Width,
                MidpointRounding.AwayFromZero);
        var newDesktopTop =
            currentWindow.Top +
            (int)Math.Round(
                relativeTop * currentWindow.Height,
                MidpointRounding.AwayFromZero);
        var newWidth = Math.Max(
            2,
            (int)Math.Round(
                relativeWidth * currentWindow.Width,
                MidpointRounding.AwayFromZero));
        var newHeight = Math.Max(
            2,
            (int)Math.Round(
                relativeHeight * currentWindow.Height,
                MidpointRounding.AwayFromZero));

        var newBoxLeft =
            newDesktopLeft - planningFrame.Left;
        var newBoxTop =
            newDesktopTop - planningFrame.Top;

        if (newBoxLeft < 0 ||
            newBoxTop < 0 ||
            newBoxLeft + newWidth > planningFrame.Width ||
            newBoxTop + newHeight > planningFrame.Height)
        {
            return new(
                decision,
                false,
                false,
                0.0,
                "Target sau remap nằm ngoài interaction frame hiện tại.");
        }

        var adjusted =
            newBoxLeft != decision.BoxLeft ||
            newBoxTop != decision.BoxTop ||
            newWidth != decision.BoxWidth ||
            newHeight != decision.BoxHeight;

        if (!adjusted)
        {
            return new(
                decision,
                false,
                true,
                0.99,
                "Geometry cửa sổ không đổi; giữ nguyên target.");
        }

        var mapped = decision with
        {
            BoxLeft = newBoxLeft,
            BoxTop = newBoxTop,
            BoxWidth = newWidth,
            BoxHeight = newHeight,
            ImageX = newBoxLeft + newWidth / 2,
            ImageY = newBoxTop + newHeight / 2
        };

        var confidence =
            Math.Abs(scaleX - 1.0) <= 0.10 &&
            Math.Abs(scaleY - 1.0) <= 0.10
                ? 0.97
                : 0.90;

        return new(
            mapped,
            true,
            true,
            confidence,
            $"Đã remap target theo cửa sổ: pos=({newBoxLeft},{newBoxTop}); size=({newWidth}x{newHeight}); scale=({scaleX:0.00},{scaleY:0.00}).");
    }

    private static bool WithinReasonableWindowRange(
        double left,
        double top,
        double width,
        double height) =>
        double.IsFinite(left) &&
        double.IsFinite(top) &&
        double.IsFinite(width) &&
        double.IsFinite(height) &&
        left >= -0.05 &&
        top >= -0.05 &&
        width > 0 &&
        height > 0 &&
        left + width <= 1.05 &&
        top + height <= 1.05;
}
