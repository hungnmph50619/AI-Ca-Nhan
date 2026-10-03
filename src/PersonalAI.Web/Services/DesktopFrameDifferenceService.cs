using System.Drawing;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDesktopFrameDifferenceService
{
    DesktopFrameDifference Compare(
        DesktopScreenshotFrame before,
        DesktopScreenshotFrame after);
}

public sealed class DesktopFrameDifferenceService : IDesktopFrameDifferenceService
{
    private const int SampleStride = 4;
    private const int ChannelDeltaThreshold = 24;

    public DesktopFrameDifference Compare(
        DesktopScreenshotFrame before,
        DesktopScreenshotFrame after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        if (!IsComparable(before, after, out var reason))
            return Unavailable(reason);

        using var beforeStream = new MemoryStream(before.Jpeg, writable: false);
        using var afterStream = new MemoryStream(after.Jpeg, writable: false);
        using var beforeBitmap = new Bitmap(beforeStream);
        using var afterBitmap = new Bitmap(afterStream);

        if (beforeBitmap.Width != afterBitmap.Width ||
            beforeBitmap.Height != afterBitmap.Height)
            return Unavailable("Kích thước bitmap trước/sau khác nhau.");

        var changed = 0;
        var total = 0;
        long deltaSum = 0;
        var minX = beforeBitmap.Width;
        var minY = beforeBitmap.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < beforeBitmap.Height; y += SampleStride)
        {
            for (var x = 0; x < beforeBitmap.Width; x += SampleStride)
            {
                var a = beforeBitmap.GetPixel(x, y);
                var b = afterBitmap.GetPixel(x, y);
                var delta =
                    (Math.Abs(a.R - b.R) +
                     Math.Abs(a.G - b.G) +
                     Math.Abs(a.B - b.B)) / 3;

                total++;
                deltaSum += delta;

                if (delta < ChannelDeltaThreshold)
                    continue;

                changed++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        var ratio = total == 0 ? 0 : (double)changed / total;
        var meanDelta = total == 0 ? 0 : (double)deltaSum / total;

        if (changed == 0)
            return new(
                true,
                0,
                0,
                total,
                0,
                0,
                0,
                0,
                meanDelta,
                "Không phát hiện thay đổi hình ảnh vượt ngưỡng nhiễu.");

        return new(
            true,
            ratio,
            changed,
            total,
            minX,
            minY,
            Math.Min(beforeBitmap.Width - minX, maxX - minX + SampleStride),
            Math.Min(beforeBitmap.Height - minY, maxY - minY + SampleStride),
            meanDelta,
            "Đã phát hiện vùng thay đổi giữa frame trước và sau hành động.");
    }

    private static bool IsComparable(
        DesktopScreenshotFrame before,
        DesktopScreenshotFrame after,
        out string reason)
    {
        if (before.Width != after.Width || before.Height != after.Height)
        {
            reason = "Kích thước frame trước/sau khác nhau.";
            return false;
        }

        if (!before.CaptureScope.Equals(after.CaptureScope, StringComparison.OrdinalIgnoreCase))
        {
            reason = "Phạm vi capture trước/sau khác nhau.";
            return false;
        }

        if (before.CaptureScope.Equals("window", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(before.WindowId, after.WindowId, StringComparison.OrdinalIgnoreCase))
        {
            reason = "WindowId trước/sau khác nhau.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(before.MonitorDevice) &&
            !string.IsNullOrWhiteSpace(after.MonitorDevice) &&
            !string.Equals(before.MonitorDevice, after.MonitorDevice, StringComparison.OrdinalIgnoreCase))
        {
            reason = "Monitor trước/sau khác nhau.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static DesktopFrameDifference Unavailable(string reason) =>
        new(false, 0, 0, 0, 0, 0, 0, 0, 0, reason);
}
