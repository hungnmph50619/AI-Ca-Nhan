using System.Drawing;
using System.Drawing.Imaging;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDesktopScreenshotService
{
    DesktopScreenshotFrame CaptureVirtualScreen();

    Task<DesktopScreenshotFrame> CaptureStableVirtualScreenAsync(
        int maximumWaitMs = 5000,
        CancellationToken cancellationToken = default);

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

    public async Task<DesktopScreenshotFrame> CaptureStableVirtualScreenAsync(
        int maximumWaitMs = 5000,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();

        var timeout = Math.Clamp(maximumWaitMs, 800, 10_000);
        var started = Environment.TickCount64;
        CaptureSample? previous = null;

        try
        {
            while (Environment.TickCount64 - started < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var current = CaptureVirtualSample();
                if (previous is not null)
                {
                    var difference = MeanSignatureDifference(
                        previous.Signature,
                        current.Signature);

                    if (difference <= 7.5)
                    {
                        previous.Frame.Clear();
                        return current.Frame;
                    }

                    previous.Frame.Clear();
                }

                previous = current;
                await Task.Delay(450, cancellationToken);
            }

            if (previous is not null)
                return previous.Frame;

            return CaptureVirtualScreen();
        }
        catch
        {
            previous?.Frame.Clear();
            throw;
        }
    }

    private CaptureSample CaptureVirtualSample()
    {
        var left = GetSystemMetrics(76);
        var top = GetSystemMetrics(77);
        var width = GetSystemMetrics(78);
        var height = GetSystemMetrics(79);
        return CaptureSampleRegion(left, top, width, height);
    }

    private CaptureSample CaptureSampleRegion(
        int left,
        int top,
        int width,
        int height)
    {
        ValidateRegion(width, height);

        using var bitmap = CaptureBitmap(left, top, width, height);
        var signature = ComputeSignature(bitmap);
        var frame = EncodeFrame(bitmap, left, top, width, height);

        return new CaptureSample(frame, signature);
    }

    private static double MeanSignatureDifference(
        IReadOnlyList<byte> first,
        IReadOnlyList<byte> second)
    {
        if (first.Count != second.Count || first.Count == 0)
            return double.MaxValue;

        long total = 0;
        for (var index = 0; index < first.Count; index++)
            total += Math.Abs(first[index] - second[index]);

        return total / (double)first.Count;
    }

    private static byte[] ComputeSignature(Bitmap bitmap)
    {
        const int columns = 16;
        const int rows = 9;
        var result = new byte[columns * rows];

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var x = Math.Clamp(
                    (int)Math.Round(
                        (column + 0.5) * bitmap.Width / columns),
                    0,
                    bitmap.Width - 1);
                var y = Math.Clamp(
                    (int)Math.Round(
                        (row + 0.5) * bitmap.Height / rows),
                    0,
                    bitmap.Height - 1);

                var pixel = bitmap.GetPixel(x, y);
                var luminance =
                    (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000;
                result[row * columns + column] = (byte)luminance;
            }
        }

        return result;
    }

    private sealed record CaptureSample(
        DesktopScreenshotFrame Frame,
        byte[] Signature);

    public DesktopScreenshotFrame CaptureRegion(
        int left,
        int top,
        int width,
        int height)
    {
        EnsureAvailable();
        ValidateRegion(width, height);

        using var bitmap = CaptureBitmap(
            left,
            top,
            width,
            height);
        return EncodeFrame(
            bitmap,
            left,
            top,
            width,
            height);
    }

    private static void ValidateRegion(
        int width,
        int height)
    {
        if (width < 64 || height < 64 || width > 12000 || height > 8000)
            throw new ToolExecutionInputException(
                "Kích thước vùng chụp không hợp lệ.");
    }

    private static Bitmap CaptureBitmap(
        int left,
        int top,
        int width,
        int height)
    {
        var bitmap = new Bitmap(
            width,
            height,
            PixelFormat.Format24bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                left,
                top,
                0,
                0,
                new Size(width, height),
                CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static DesktopScreenshotFrame EncodeFrame(
        Bitmap bitmap,
        int left,
        int top,
        int width,
        int height)
    {
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
