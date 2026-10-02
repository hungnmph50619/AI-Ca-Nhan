using System.Drawing;
using System.Drawing.Imaging;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDesktopScreenshotService
{
    DesktopScreenshotFrame CaptureVirtualScreen();
}

public sealed class WindowsDesktopScreenshotService : IDesktopScreenshotService
{
    public DesktopScreenshotFrame CaptureVirtualScreen()
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            throw new ToolExecutionInputException(
                "Chụp màn hình desktop chỉ khả dụng trong phiên Windows đang tương tác.");

        var left = GetSystemMetrics(76);
        var top = GetSystemMetrics(77);
        var width = GetSystemMetrics(78);
        var height = GetSystemMetrics(79);
        if (width < 64 || height < 64 || width > 12000 || height > 8000)
            throw new ToolExecutionInputException(
                "Kích thước desktop ảo hiện tại không hợp lệ để chụp ảnh.");

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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
