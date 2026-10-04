using System.Drawing;
using System.Drawing.Imaging;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record DesktopVisionFrameSelection(
    DesktopScreenshotFrame Frame,
    string Source,
    bool OwnsFrame)
{
    public void Clear()
    {
        if (OwnsFrame)
            Frame.Clear();
    }
}

public interface IDesktopRoiVisionService
{
    DesktopVisionFrameSelection SelectVerificationFrame(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame planningFrame,
        DesktopScreenshotFrame currentFrame,
        DesktopFrameDifference? frameDifference);
}

public sealed class DesktopRoiVisionService
    : IDesktopRoiVisionService
{
    private const int MarginPixels = 160;
    private const int MinimumRoiSize = 96;

    public DesktopVisionFrameSelection SelectVerificationFrame(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame planningFrame,
        DesktopScreenshotFrame currentFrame,
        DesktopFrameDifference? frameDifference)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(planningFrame);
        ArgumentNullException.ThrowIfNull(currentFrame);

        if (CanReusePlanningPixelBox(
                decision,
                planningFrame,
                currentFrame) &&
            TryBuildExpandedBox(
                decision.BoxLeft,
                decision.BoxTop,
                decision.BoxWidth,
                decision.BoxHeight,
                currentFrame.Width,
                currentFrame.Height,
                out var targetBox))
        {
            return new(
                Crop(currentFrame, targetBox, "target-roi"),
                "target-roi",
                true);
        }

        if (frameDifference?.Comparable == true &&
            frameDifference.HasMeaningfulChange &&
            TryBuildExpandedBox(
                frameDifference.BoxLeft,
                frameDifference.BoxTop,
                frameDifference.BoxWidth,
                frameDifference.BoxHeight,
                currentFrame.Width,
                currentFrame.Height,
                out var changedBox))
        {
            return new(
                Crop(currentFrame, changedBox, "changed-region"),
                "changed-region",
                true);
        }

        if (currentFrame.CaptureScope.Equals(
                "window",
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                currentFrame,
                "active-window",
                false);
        }

        return new(
            currentFrame,
            "full-frame-fallback",
            false);
    }

    private static bool CanReusePlanningPixelBox(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame planningFrame,
        DesktopScreenshotFrame currentFrame)
    {
        if (!decision.CoordinateSpace.Equals(
                ComputerCoordinateSpaces.ImagePixel,
                StringComparison.OrdinalIgnoreCase) ||
            decision.BoxWidth <= 0 ||
            decision.BoxHeight <= 0)
            return false;

        return planningFrame.Left == currentFrame.Left &&
               planningFrame.Top == currentFrame.Top &&
               planningFrame.Width == currentFrame.Width &&
               planningFrame.Height == currentFrame.Height;
    }

    private static bool TryBuildExpandedBox(
        int left,
        int top,
        int width,
        int height,
        int frameWidth,
        int frameHeight,
        out Rectangle box)
    {
        box = Rectangle.Empty;

        if (width <= 0 ||
            height <= 0 ||
            frameWidth < MinimumRoiSize ||
            frameHeight < MinimumRoiSize)
            return false;

        var requestedLeft = Math.Max(
            0,
            left - MarginPixels);
        var requestedTop = Math.Max(
            0,
            top - MarginPixels);
        var requestedRight = Math.Min(
            frameWidth,
            left + width + MarginPixels);
        var requestedBottom = Math.Min(
            frameHeight,
            top + height + MarginPixels);

        var roiWidth = requestedRight - requestedLeft;
        var roiHeight = requestedBottom - requestedTop;

        if (roiWidth < MinimumRoiSize ||
            roiHeight < MinimumRoiSize)
            return false;

        box = new Rectangle(
            requestedLeft,
            requestedTop,
            roiWidth,
            roiHeight);
        return true;
    }

    private static DesktopScreenshotFrame Crop(
        DesktopScreenshotFrame source,
        Rectangle box,
        string sourceLabel)
    {
        using var input = new MemoryStream(
            source.Jpeg,
            writable: false);
        using var bitmap = new Bitmap(input);
        using var cropped = bitmap.Clone(
            box,
            PixelFormat.Format24bppRgb);
        using var output = new MemoryStream();

        var codec = ImageCodecInfo.GetImageEncoders()
            .First(item =>
                item.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(
            Encoder.Quality,
            88L);

        cropped.Save(
            output,
            codec,
            parameters);

        return source with
        {
            Jpeg = output.ToArray(),
            Left = source.Left + box.Left,
            Top = source.Top + box.Top,
            Width = box.Width,
            Height = box.Height,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            CaptureScope = "roi",
            CaptureBackend = $"{source.CaptureBackend}+{sourceLabel}",
            CaptureFallbackReason =
                $"ROI từ {source.CaptureScope}; origin=({source.Left},{source.Top}); roi=({box.Left},{box.Top},{box.Width},{box.Height})."
        };
    }
}
