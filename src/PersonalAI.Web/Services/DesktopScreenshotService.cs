using System.Drawing;
using System.Drawing.Imaging;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDesktopScreenshotService
{
    DesktopScreenshotFrame CaptureVirtualScreen();

    DesktopScreenshotFrame CaptureRegion(
        int left,
        int top,
        int width,
        int height);
}

public sealed class WindowsDesktopScreenshotService : IDesktopScreenshotService
{
    public DesktopScreenshotFrame CaptureVirtualScreen()
    {
        EnsureAvailable();

        var left = GetSystemMetrics(76);
        var top = GetSystemMetrics(77);
        var width = GetSystemMetrics(78);
        var height = GetSystemMetrics(79);
        return CaptureRegion(left, top, width, height);
    }

    public DesktopScreenshotFrame CaptureRegion(
        int left,
        int top,
        int width,
        int height)
    {
        EnsureAvailable();

        if (width < 64 || height < 64 || width > 12000 || height > 8000)
            throw new ToolExecutionInputException(
                "Kích thước vùng chụp không hợp lệ.");

        using var bitmap = new Bitmap(
            width,
            height,
            PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                left,
                top,
                0,
                0,
                new Size(width, height),
                CopyPixelOperation.SourceCopy);
        }

        using var memory = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders()
            .First(item => item.FormatID == ImageFormat.Jpeg.Guid);
        using var quality = new EncoderParameters(1);
        quality.Param[0] = new EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality,
            72L);
        bitmap.Save(memory, encoder, quality);

        var bytes = memory.ToArray();
        if (bytes.Length < 24 || bytes.Length > 4 * 1024 * 1024)
            throw new ToolExecutionInputException(
                "Ảnh chụp desktop vượt giới hạn xử lý 4 MB.");

        return new(
            bytes,
            left,
            top,
            width,
            height,
            DateTimeOffset.UtcNow);
    }

    private static void EnsureAvailable()
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            throw new ToolExecutionInputException(
                "Chụp màn hình desktop chỉ khả dụng trong phiên Windows đang tương tác.");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
