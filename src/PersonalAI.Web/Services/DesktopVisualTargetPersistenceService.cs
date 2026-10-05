using System.Drawing;
using System.Drawing.Imaging;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record DesktopVisualTargetTemplate(
    bool Available,
    byte[] Image,
    int SourceLeft,
    int SourceTop,
    int SourceWidth,
    int SourceHeight,
    DateTimeOffset CapturedAtUtc,
    string Provider,
    string Reason);

public sealed record DesktopVisualTargetRelocation(
    bool Available,
    bool Relocated,
    bool Ambiguous,
    double Confidence,
    int Left,
    int Top,
    int Width,
    int Height,
    double Scale,
    string Provider,
    string Reason);

public interface IDesktopVisualTargetPersistenceService
{
    DesktopVisualTargetTemplate CreateTemplate(
        DesktopScreenshotFrame frame,
        int left,
        int top,
        int width,
        int height);

    DesktopVisualTargetRelocation Relocate(
        DesktopScreenshotFrame frame,
        DesktopVisualTargetTemplate template,
        double minimumScore = 0.90);
}

public sealed class DesktopVisualTargetPersistenceService(
    IDesktopTemplateMatchingSensor matcher)
    : IDesktopVisualTargetPersistenceService
{
    private const int MinimumTemplateDimension = 6;
    private const int MaximumTemplateDimension = 512;

    public DesktopVisualTargetTemplate CreateTemplate(
        DesktopScreenshotFrame frame,
        int left,
        int top,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!IsSafeTemplateBox(
                frame.Width,
                frame.Height,
                left,
                top,
                width,
                height))
        {
            return UnavailableTemplate(
                "Target box không đủ an toàn để tạo visual template.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return UnavailableTemplate(
                "Visual target template hiện chỉ bật trên Windows.");
        }

        if (frame.Jpeg is null ||
            frame.Jpeg.Length == 0)
        {
            return UnavailableTemplate(
                "Frame không có JPEG để tạo visual target template.");
        }

        try
        {
            using var stream =
                new MemoryStream(
                    frame.Jpeg,
                    writable: false);
            using var source =
                new Bitmap(
                    stream);

            if (source.Width !=
                    frame.Width ||
                source.Height !=
                    frame.Height)
            {
                return UnavailableTemplate(
                    "JPEG frame không khớp geometry đã quan sát.");
            }

            using var roi =
                source.Clone(
                    new Rectangle(
                        left,
                        top,
                        width,
                        height),
                    source.PixelFormat);

            using var output =
                new MemoryStream();

            roi.Save(
                output,
                ImageFormat.Png);

            var encoded =
                output.ToArray();

            if (encoded.Length == 0)
            {
                return UnavailableTemplate(
                    "Không encode được target template.");
            }

            return new(
                Available: true,
                Image:
                    encoded,
                SourceLeft:
                    left,
                SourceTop:
                    top,
                SourceWidth:
                    width,
                SourceHeight:
                    height,
                CapturedAtUtc:
                    frame.CapturedAtUtc,
                Provider:
                    "opencv-template",
                Reason:
                    "Đã lưu visual target template từ ROI đã xác minh; OpenCV native chỉ chạy trong worker.");
        }
        catch (Exception exception) when (
            exception is
                ArgumentException or
                InvalidOperationException or
                ExternalException or
                PlatformNotSupportedException)
        {
            return UnavailableTemplate(
                $"Không tạo được visual template: {exception.GetType().Name}: {exception.Message}");
        }
    }

    public DesktopVisualTargetRelocation Relocate(
        DesktopScreenshotFrame frame,
        DesktopVisualTargetTemplate template,
        double minimumScore = 0.90)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(template);

        if (!template.Available ||
            template.Image.Length == 0)
        {
            return UnavailableRelocation(
                "Visual target template không khả dụng.");
        }

        var result =
            matcher.Match(
                frame,
                template.Image,
                minimumScore);

        if (!result.Available)
        {
            return UnavailableRelocation(
                result.Reason);
        }

        if (result.Ambiguous)
        {
            return new(
                Available: true,
                Relocated: false,
                Ambiguous: true,
                Confidence:
                    Math.Clamp(
                        result.Score,
                        0,
                        1),
                Left: 0,
                Top: 0,
                Width: 0,
                Height: 0,
                Scale:
                    result.Scale,
                Provider:
                    result.Provider,
                Reason:
                    result.Reason);
        }

        if (!result.Matched)
        {
            return new(
                Available: true,
                Relocated: false,
                Ambiguous: false,
                Confidence:
                    Math.Clamp(
                        result.Score,
                        0,
                        1),
                Left: 0,
                Top: 0,
                Width: 0,
                Height: 0,
                Scale:
                    result.Scale,
                Provider:
                    result.Provider,
                Reason:
                    result.Reason);
        }

        return new(
            Available: true,
            Relocated: true,
            Ambiguous: false,
            Confidence:
                Math.Clamp(
                    result.Score,
                    0,
                    1),
            Left:
                result.Left,
            Top:
                result.Top,
            Width:
                result.Width,
            Height:
                result.Height,
            Scale:
                result.Scale,
            Provider:
                result.Provider,
            Reason:
                $"Visual target persistence reacquire thành công. {result.Reason}");
    }

    internal static bool IsSafeTemplateBox(
        int frameWidth,
        int frameHeight,
        int left,
        int top,
        int width,
        int height) =>
        frameWidth > 0 &&
        frameHeight > 0 &&
        left >= 0 &&
        top >= 0 &&
        width >=
            MinimumTemplateDimension &&
        height >=
            MinimumTemplateDimension &&
        width <=
            MaximumTemplateDimension &&
        height <=
            MaximumTemplateDimension &&
        left + width <=
            frameWidth &&
        top + height <=
            frameHeight;

    private static DesktopVisualTargetTemplate UnavailableTemplate(
        string reason) =>
        new(
            Available: false,
            Image:
                Array.Empty<byte>(),
            SourceLeft: 0,
            SourceTop: 0,
            SourceWidth: 0,
            SourceHeight: 0,
            CapturedAtUtc:
                DateTimeOffset.UtcNow,
            Provider:
                "opencv-template",
            Reason:
                reason);

    private static DesktopVisualTargetRelocation UnavailableRelocation(
        string reason) =>
        new(
            Available: false,
            Relocated: false,
            Ambiguous: false,
            Confidence: 0,
            Left: 0,
            Top: 0,
            Width: 0,
            Height: 0,
            Scale: 1,
            Provider:
                "opencv-template",
            Reason:
                reason);
}
