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

    DesktopScreenshotFrame CaptureWindow(
        string windowId);

    Task<DesktopScreenshotFrame> CaptureStableWindowAsync(
        string windowId,
        int maximumWaitMs = 5000,
        CancellationToken cancellationToken = default);

    DesktopScreenshotFrame CaptureRegion(
        int left,
        int top,
        int width,
        int height);
}

public sealed class WindowsDesktopScreenshotService(
    WindowsAiOperatorConsoleService operatorConsole,
    IComputerUseService computer)
    : IDesktopScreenshotService
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

    public DesktopScreenshotFrame CaptureWindow(
        string windowId)
    {
        EnsureAvailable();

        var window = ResolveWindow(windowId);
        var region = GetVisibleWindowRegion(window);

        using var bitmap = CaptureBitmap(
            region.Left,
            region.Top,
            region.Width,
            region.Height);
        MaskOperatorConsole(
            bitmap,
            region.Left,
            region.Top);

        var frame = EncodeFrame(
            bitmap,
            region.Left,
            region.Top,
            region.Width,
            region.Height);

        return frame with
        {
            CaptureScope = "window",
            WindowId = window.WindowId,
            WindowTitle = window.Title,
            WindowWasForeground = window.IsForeground
        };
    }

    public async Task<DesktopScreenshotFrame> CaptureStableWindowAsync(
        string windowId,
        int maximumWaitMs = 5000,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();

        var timeout = Math.Clamp(
            maximumWaitMs,
            800,
            10_000);
        var started = Environment.TickCount64;
        CaptureSample? previous = null;

        try
        {
            while (Environment.TickCount64 - started < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var current = CaptureWindowSample(
                    windowId);

                if (previous is not null)
                {
                    var sameGeometry =
                        previous.Frame.Left == current.Frame.Left &&
                        previous.Frame.Top == current.Frame.Top &&
                        previous.Frame.Width == current.Frame.Width &&
                        previous.Frame.Height == current.Frame.Height;

                    var difference = sameGeometry
                        ? MeanSignatureDifference(
                            previous.Signature,
                            current.Signature)
                        : double.MaxValue;

                    if (difference <= 7.5)
                    {
                        previous.Frame.Clear();
                        return current.Frame;
                    }

                    previous.Frame.Clear();
                }

                previous = current;
                await Task.Delay(
                    450,
                    cancellationToken);
            }

            if (previous is not null)
                return previous.Frame;

            return CaptureWindow(windowId);
        }
        catch
        {
            previous?.Frame.Clear();
            throw;
        }
    }

    private CaptureSample CaptureWindowSample(
        string windowId)
    {
        var window = ResolveWindow(windowId);
        var region = GetVisibleWindowRegion(window);

        using var bitmap = CaptureBitmap(
            region.Left,
            region.Top,
            region.Width,
            region.Height);
        MaskOperatorConsole(
            bitmap,
            region.Left,
            region.Top);

        var signature = ComputeSignature(bitmap);
        var frame = EncodeFrame(
            bitmap,
            region.Left,
            region.Top,
            region.Width,
            region.Height) with
        {
            CaptureScope = "window",
            WindowId = window.WindowId,
            WindowTitle = window.Title,
            WindowWasForeground = window.IsForeground
        };

        return new(
            frame,
            signature);
    }

    private ComputerWindowInfo ResolveWindow(
        string windowId)
    {
        var id = (windowId ?? string.Empty).Trim();
        if (id.Length == 0)
            throw new ToolExecutionInputException(
                "Thiếu windowId cho ảnh chụp cửa sổ.");

        return computer.GetWindows(50).Windows
            .FirstOrDefault(window =>
                window.WindowId.Equals(
                    id,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new ToolExecutionInputException(
                "Cửa sổ cần chụp không còn tồn tại hoặc không còn hiển thị.");
    }

    private (int Left, int Top, int Width, int Height) GetVisibleWindowRegion(
        ComputerWindowInfo window)
    {
        if (window.Width <= 0 ||
            window.Height <= 0)
            throw new ToolExecutionInputException(
                "Cửa sổ cần chụp không có kích thước hợp lệ.");

        var screen = computer.GetScreenInfo();
        var virtualRight = checked(
            screen.VirtualLeft + screen.VirtualWidth);
        var virtualBottom = checked(
            screen.VirtualTop + screen.VirtualHeight);
        var windowRight = checked(
            window.Left + window.Width);
        var windowBottom = checked(
            window.Top + window.Height);

        var left = Math.Max(
            screen.VirtualLeft,
            window.Left);
        var top = Math.Max(
            screen.VirtualTop,
            window.Top);
        var right = Math.Min(
            virtualRight,
            windowRight);
        var bottom = Math.Min(
            virtualBottom,
            windowBottom);

        var width = right - left;
        var height = bottom - top;

        ValidateRegion(
            width,
            height);

        return (
            left,
            top,
            width,
            height);
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
        MaskOperatorConsole(bitmap, left, top);
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
        MaskOperatorConsole(bitmap, left, top);
        return EncodeFrame(
            bitmap,
            left,
            top,
            width,
            height);
    }

    private void MaskOperatorConsole(
        Bitmap bitmap,
        int captureLeft,
        int captureTop)
    {
        if (!operatorConsole.TryGetVisibleBounds(
            out var consoleLeft,
            out var consoleTop,
            out var consoleWidth,
            out var consoleHeight))
            return;

        var captureRight = captureLeft + bitmap.Width;
        var captureBottom = captureTop + bitmap.Height;
        var consoleRight = consoleLeft + consoleWidth;
        var consoleBottom = consoleTop + consoleHeight;

        var overlapLeft = Math.Max(captureLeft, consoleLeft);
        var overlapTop = Math.Max(captureTop, consoleTop);
        var overlapRight = Math.Min(captureRight, consoleRight);
        var overlapBottom = Math.Min(captureBottom, consoleBottom);

        if (overlapRight <= overlapLeft || overlapBottom <= overlapTop)
            return;

        var localX = overlapLeft - captureLeft;
        var localY = overlapTop - captureTop;
        var localWidth = overlapRight - overlapLeft;
        var localHeight = overlapBottom - overlapTop;

        using var graphics = Graphics.FromImage(bitmap);
        using var brush = new SolidBrush(Color.FromArgb(18, 18, 18));
        graphics.FillRectangle(
            brush,
            localX,
            localY,
            localWidth,
            localHeight);
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
