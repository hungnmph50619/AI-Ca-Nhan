using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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

    DesktopScreenshotFrame CaptureMonitor(
        string deviceName);

    Task<DesktopScreenshotFrame> CaptureStableMonitorAsync(
        string deviceName,
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
    IComputerUseService computer,
    IComputerDisplayTopologyService displays,
    IComputerWindowVisibilityService visibility,
    IDesktopCaptureBackendRouter captureRouter,
    IWindowsGraphicsCaptureClient wgc,
    IDxgiDesktopDuplicationClient dxgi,
    IDesktopCaptureHealthTracker captureHealth)
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

    public DesktopScreenshotFrame CaptureMonitor(
        string deviceName)
    {
        EnsureAvailable();

        var monitor = ResolveMonitor(
            deviceName);
        return CaptureMonitorFrame(
            monitor);
    }

    public async Task<DesktopScreenshotFrame> CaptureStableMonitorAsync(
        string deviceName,
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

                var monitor = ResolveMonitor(
                    deviceName);
                var current = CaptureMonitorSample(
                    monitor);

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

            return CaptureMonitor(deviceName);
        }
        catch
        {
            previous?.Frame.Clear();
            throw;
        }
    }

    private CaptureSample CaptureMonitorSample(
        ComputerMonitorInfo monitor)
    {
        var result = CaptureMonitorBitmap(
            monitor);

        using var bitmap = result.Bitmap;
        var signature = ComputeSignature(
            bitmap);

        captureHealth.RecordSuccess(
            DesktopCaptureScopes.Monitor,
            monitor.DeviceName,
            result.Backend,
            result.LatencyMs,
            signature,
            result.FallbackReason);

        var frame = EncodeMonitorFrame(
            bitmap,
            monitor,
            result.Backend,
            result.FallbackReason);

        return new(
            frame,
            signature);
    }

    private DesktopScreenshotFrame CaptureMonitorFrame(
        ComputerMonitorInfo monitor)
    {
        var result = CaptureMonitorBitmap(
            monitor);

        using var bitmap = result.Bitmap;
        var signature = ComputeSignature(
            bitmap);

        captureHealth.RecordSuccess(
            DesktopCaptureScopes.Monitor,
            monitor.DeviceName,
            result.Backend,
            result.LatencyMs,
            signature,
            result.FallbackReason);

        return EncodeMonitorFrame(
            bitmap,
            monitor,
            result.Backend,
            result.FallbackReason);
    }

    private MonitorCaptureBitmap CaptureMonitorBitmap(
        ComputerMonitorInfo monitor)
    {
        var stopwatch = Stopwatch.StartNew();

        ValidateRegion(
            monitor.Width,
            monitor.Height);

        string? wgcReason = null;
        var candidates =
            captureRouter.GetOrderedCandidates(
                DesktopCaptureScopes.Monitor);

        if (candidates.Contains(
                DesktopCaptureBackends.WindowsGraphicsCapture,
                StringComparer.OrdinalIgnoreCase) &&
            wgc.Available)
        {
            var captured =
                wgc.CaptureMonitor(
                    monitor.DeviceName);

            if (captured.Success &&
                TryDecodeJpeg(
                    captured.Jpeg,
                    out var wgcBitmap) &&
                wgcBitmap is not null)
            {
                if (wgcBitmap.Width == monitor.Width &&
                    wgcBitmap.Height == monitor.Height)
                {
                    MaskOperatorConsole(
                        wgcBitmap,
                        monitor.Left,
                        monitor.Top);

                    stopwatch.Stop();

                    return new(
                        wgcBitmap,
                        DesktopCaptureBackends.WindowsGraphicsCapture,
                        null,
                        stopwatch.ElapsedMilliseconds);
                }

                wgcReason =
                    $"WGC monitor trả geometry {wgcBitmap.Width}x{wgcBitmap.Height}, khác topology {monitor.Width}x{monitor.Height}.";

                wgcBitmap.Dispose();
            }
            else
            {
                wgcReason =
                    captured.Detail;
            }
        }

        string? dxgiReason = null;

        if (candidates.Contains(
                DesktopCaptureBackends.DxgiDesktopDuplication,
                StringComparer.OrdinalIgnoreCase) &&
            dxgi.Available)
        {
            var captured =
                dxgi.CaptureMonitor(
                    monitor.DeviceName);

            if (captured.Success &&
                TryDecodeJpeg(
                    captured.Jpeg,
                    out var dxgiBitmap) &&
                dxgiBitmap is not null)
            {
                if (dxgiBitmap.Width == monitor.Width &&
                    dxgiBitmap.Height == monitor.Height)
                {
                    MaskOperatorConsole(
                        dxgiBitmap,
                        monitor.Left,
                        monitor.Top);

                    stopwatch.Stop();

                    return new(
                        dxgiBitmap,
                        DesktopCaptureBackends.DxgiDesktopDuplication,
                        string.IsNullOrWhiteSpace(wgcReason)
                            ? null
                            : $"WGC fallback: {wgcReason}",
                        stopwatch.ElapsedMilliseconds);
                }

                dxgiReason =
                    $"DXGI monitor trả geometry {dxgiBitmap.Width}x{dxgiBitmap.Height}, khác topology {monitor.Width}x{monitor.Height}.";

                dxgiBitmap.Dispose();
            }
            else
            {
                dxgiReason =
                    captured.Detail;
            }
        }

        var bitmap = CaptureBitmap(
            monitor.Left,
            monitor.Top,
            monitor.Width,
            monitor.Height);

        MaskOperatorConsole(
            bitmap,
            monitor.Left,
            monitor.Top);

        var fallbackReasons = new List<string>();

        if (!string.IsNullOrWhiteSpace(wgcReason))
            fallbackReasons.Add($"WGC: {wgcReason}");

        if (!string.IsNullOrWhiteSpace(dxgiReason))
            fallbackReasons.Add($"DXGI: {dxgiReason}");

        stopwatch.Stop();

        return new(
            bitmap,
            DesktopCaptureBackends.CopyFromScreen,
            fallbackReasons.Count == 0
                ? null
                : string.Join(" ", fallbackReasons),
            stopwatch.ElapsedMilliseconds);
    }

    private static DesktopScreenshotFrame EncodeMonitorFrame(
        Bitmap bitmap,
        ComputerMonitorInfo monitor,
        string backend,
        string? fallbackReason)
    {
        var frame = EncodeFrame(
            bitmap,
            monitor.Left,
            monitor.Top,
            monitor.Width,
            monitor.Height);

        return frame with
        {
            CaptureScope = "monitor",
            CaptureBackend = backend,
            CaptureFallbackReason = fallbackReason,
            MonitorDevice = monitor.DeviceName,
            MonitorWasPrimary = monitor.Primary,
            MonitorDpiX = monitor.DpiX,
            MonitorDpiY = monitor.DpiY
        };
    }

    private sealed record MonitorCaptureBitmap(
        Bitmap Bitmap,
        string Backend,
        string? FallbackReason,
        long LatencyMs);

    private ComputerMonitorInfo ResolveMonitor(
        string deviceName)
    {
        var normalized = (deviceName ?? string.Empty).Trim();
        if (normalized.Length == 0)
            throw new ToolExecutionInputException(
                "Thiếu tên màn hình cần chụp.");

        var topology = displays.GetTopology();
        return topology.Monitors.FirstOrDefault(
                   monitor => monitor.DeviceName.Equals(
                       normalized,
                       StringComparison.OrdinalIgnoreCase))
               ?? throw new ToolExecutionInputException(
                   $"Không tìm thấy màn hình “{normalized}”.");
    }

    public DesktopScreenshotFrame CaptureWindow(
        string windowId)
    {
        EnsureAvailable();

        var window = ResolveWindow(windowId);
        return CaptureWindowFrame(window);
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
        var result = CaptureWindowBitmap(window);

        using var bitmap = result.Bitmap;
        var signature = ComputeSignature(bitmap);
        var frame = EncodeWindowFrame(
            bitmap,
            window,
            result.Left,
            result.Top,
            result.Backend,
            result.FallbackReason);

        return new(
            frame,
            signature);
    }

    private DesktopScreenshotFrame CaptureWindowFrame(
        ComputerWindowInfo window)
    {
        var result = CaptureWindowBitmap(window);
        using var bitmap = result.Bitmap;

        return EncodeWindowFrame(
            bitmap,
            window,
            result.Left,
            result.Top,
            result.Backend,
            result.FallbackReason);
    }

    private WindowCaptureBitmap CaptureWindowBitmap(
        ComputerWindowInfo window)
    {
        string? wgcReason = null;
        var candidates =
            captureRouter.GetOrderedCandidates(
                DesktopCaptureScopes.Window);

        if (candidates.Contains(
                DesktopCaptureBackends.WindowsGraphicsCapture,
                StringComparer.OrdinalIgnoreCase) &&
            wgc.Available)
        {
            var captured =
                wgc.CaptureWindow(
                    window.WindowId);

            if (captured.Success &&
                TryDecodeJpeg(
                    captured.Jpeg,
                    out var wgcBitmap))
            {
                return new(
                    wgcBitmap!,
                    window.Left,
                    window.Top,
                    DesktopCaptureBackends.WindowsGraphicsCapture,
                    null);
            }

            wgcReason =
                captured.Detail;
        }

        string? printWindowReason = null;
        Bitmap? printWindowBitmap = null;
        var canUseFullWindowCapture =
            CanUseFullWindowCapture(window);
        var allowPrintWindow =
            candidates.Contains(
                DesktopCaptureBackends.PrintWindow,
                StringComparer.OrdinalIgnoreCase);

        if (allowPrintWindow &&
            canUseFullWindowCapture &&
            TryCaptureWithPrintWindow(
                window,
                out printWindowBitmap,
                out printWindowReason))
        {
            return new(
                printWindowBitmap!,
                window.Left,
                window.Top,
                DesktopCaptureBackends.PrintWindow,
                string.IsNullOrWhiteSpace(wgcReason)
                    ? null
                    : $"WGC fallback: {wgcReason}");
        }

        var fallbackReason = canUseFullWindowCapture
            ? "PrintWindow không trả frame hữu dụng."
            : "Cửa sổ nằm một phần ngoài desktop ảo; dùng vùng nhìn thấy.";

        var region = GetVisibleWindowRegion(window);
        var screenBitmap = CaptureBitmap(
            region.Left,
            region.Top,
            region.Width,
            region.Height);
        MaskOperatorConsole(
            screenBitmap,
            region.Left,
            region.Top);

        var reasons = new List<string>();
        if (!string.IsNullOrWhiteSpace(wgcReason))
            reasons.Add($"WGC: {wgcReason}");
        if (!string.IsNullOrWhiteSpace(printWindowReason))
            reasons.Add($"PrintWindow: {printWindowReason}");
        reasons.Add(fallbackReason);

        return new(
            screenBitmap,
            region.Left,
            region.Top,
            DesktopCaptureBackends.CopyFromScreen,
            string.Join(" ", reasons));
    }

    private static bool TryDecodeJpeg(
        byte[] jpeg,
        out Bitmap? bitmap)
    {
        bitmap = null;

        if (jpeg is null ||
            jpeg.Length < 24)
        {
            return false;
        }

        try
        {
            using var memory =
                new MemoryStream(
                    jpeg,
                    writable: false);

            using var source =
                new Bitmap(
                    memory);

            bitmap = new Bitmap(
                source.Width,
                source.Height,
                PixelFormat.Format24bppRgb);

            using var graphics =
                Graphics.FromImage(
                    bitmap);

            graphics.DrawImageUnscaled(
                source,
                0,
                0);

            return bitmap.Width > 0 &&
                   bitmap.Height > 0;
        }
        catch (Exception exception) when (
            exception is
                ArgumentException or
                ExternalException)
        {
            bitmap?.Dispose();
            bitmap = null;
            return false;
        }
    }

    private bool CanUseFullWindowCapture(
        ComputerWindowInfo window)
    {
        var screen = computer.GetScreenInfo();
        var virtualRight = checked(
            screen.VirtualLeft + screen.VirtualWidth);
        var virtualBottom = checked(
            screen.VirtualTop + screen.VirtualHeight);

        return window.Width >= 64 &&
               window.Height >= 64 &&
               window.Width <= 12000 &&
               window.Height <= 8000 &&
               window.Left >= screen.VirtualLeft &&
               window.Top >= screen.VirtualTop &&
               window.Left + window.Width <= virtualRight &&
               window.Top + window.Height <= virtualBottom;
    }

    private static bool TryCaptureWithPrintWindow(
        ComputerWindowInfo window,
        out Bitmap? bitmap,
        out string? reason)
    {
        bitmap = null;
        reason = null;

        if (!TryParseWindowHandle(
                window.WindowId,
                out var handle))
        {
            reason = "WindowId không chuyển được thành HWND.";
            return false;
        }

        var candidate = new Bitmap(
            window.Width,
            window.Height,
            PixelFormat.Format24bppRgb);

        try
        {
            using var graphics = Graphics.FromImage(candidate);
            var hdc = graphics.GetHdc();

            bool captured;
            try
            {
                captured = PrintWindow(
                    handle,
                    hdc,
                    PwRenderFullContent);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            if (!captured)
            {
                candidate.Dispose();
                reason = "PrintWindow trả false.";
                return false;
            }

            if (!HasUsefulContent(candidate))
            {
                candidate.Dispose();
                reason = "PrintWindow trả frame gần như rỗng.";
                return false;
            }

            bitmap = candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is
                System.ComponentModel.Win32Exception or
                ArgumentException or
                ExternalException)
        {
            candidate.Dispose();
            reason = $"PrintWindow lỗi: {exception.GetType().Name}.";
            return false;
        }
    }

    private static bool HasUsefulContent(
        Bitmap bitmap)
    {
        const int columns = 12;
        const int rows = 8;
        long total = 0;
        var maximum = 0;

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
                    (pixel.R * 299 +
                     pixel.G * 587 +
                     pixel.B * 114) / 1000;

                total += luminance;
                maximum = Math.Max(
                    maximum,
                    luminance);
            }
        }

        var sampleCount = columns * rows;
        var mean = total / (double)sampleCount;

        // PrintWindow thường trả bitmap toàn đen khi app/GPU surface
        // không hỗ trợ backend này. Những frame đó phải fallback.
        return maximum >= 12 || mean >= 8;
    }

    private DesktopScreenshotFrame EncodeWindowFrame(
        Bitmap bitmap,
        ComputerWindowInfo window,
        int left,
        int top,
        string backend,
        string? fallbackReason)
    {
        var frame = EncodeFrame(
            bitmap,
            left,
            top,
            bitmap.Width,
            bitmap.Height);

        var centerX = window.Left + Math.Max(
            0,
            window.Width / 2);
        var centerY = window.Top + Math.Max(
            0,
            window.Height / 2);
        var monitor = displays.GetMonitorAtPoint(
            centerX,
            centerY);
        var visibilityAssessment = visibility.Assess(window);

        return frame with
        {
            CaptureScope = "window",
            WindowId = window.WindowId,
            WindowTitle = window.Title,
            WindowWasForeground = window.IsForeground,
            CaptureBackend = backend,
            CaptureFallbackReason = fallbackReason,
            MonitorDevice = monitor?.DeviceName,
            MonitorWasPrimary = monitor?.Primary ?? false,
            MonitorDpiX = monitor?.DpiX ?? 96,
            MonitorDpiY = monitor?.DpiY ?? 96,
            WindowVisibleRatio = visibilityAssessment.VisibleRatio,
            WindowLikelyOccluded = visibilityAssessment.LikelyOccluded
        };
    }

    private static bool TryParseWindowHandle(
        string windowId,
        out IntPtr handle)
    {
        var value = (windowId ?? string.Empty).Trim();
        if (value.StartsWith(
                "0x",
                StringComparison.OrdinalIgnoreCase))
            value = value[2..];

        if (value.Length == 0 ||
            !ulong.TryParse(
                value,
                System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture,
                out var raw) ||
            raw == 0)
        {
            handle = IntPtr.Zero;
            return false;
        }

        handle = new IntPtr(
            unchecked((long)raw));
        return true;
    }

    private sealed record WindowCaptureBitmap(
        Bitmap Bitmap,
        int Left,
        int Top,
        string Backend,
        string? FallbackReason);

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
        EnsureCurrentRegionBackend(
            DesktopCaptureScopes.VirtualDesktop);

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

        var backend =
            captureRouter.GetPreferredAvailableBackend(
                DesktopCaptureScopes.Region);
        if (!backend.Equals(
                DesktopCaptureBackends.CopyFromScreen,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Backend {backend} chưa có adapter capture region.");
        }

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
            height) with
        {
            CaptureBackend = backend
        };
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

    private void EnsureCurrentRegionBackend(
        string scope)
    {
        var backend =
            captureRouter.GetPreferredAvailableBackend(scope);

        if (!backend.Equals(
                DesktopCaptureBackends.CopyFromScreen,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Backend {backend} đã được chọn cho {scope} nhưng adapter chưa được nối vào WindowsDesktopScreenshotService.");
        }
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

    private const uint PwRenderFullContent = 0x00000002;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(
        IntPtr hWnd,
        IntPtr hdcBlt,
        uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
