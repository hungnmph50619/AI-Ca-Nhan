using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerCoordinateTransformService
{
    ComputerCoordinatePoint ToDesktopPoint(
        ComputerCoordinateRequest request,
        DesktopScreenshotFrame frame);
}

public sealed class ComputerCoordinateTransformService(
    IComputerUseService computer,
    IComputerDisplayTopologyService displays) : IComputerCoordinateTransformService
{
    public ComputerCoordinatePoint ToDesktopPoint(
        ComputerCoordinateRequest request,
        DesktopScreenshotFrame frame)
    {
        var space = (request.Space ?? string.Empty).Trim().ToLowerInvariant();
        if (!ComputerCoordinateSpaces.All.Contains(space))
            throw new ToolExecutionInputException(
                $"Hệ tọa độ không được hỗ trợ: {space}.");

        var screen = computer.GetScreenInfo();

        var point = space switch
        {
            ComputerCoordinateSpaces.ImagePixel =>
                FromImagePixel(request, frame),

            ComputerCoordinateSpaces.ImageNormalized =>
                FromImageNormalized(request, frame),

            ComputerCoordinateSpaces.WindowNormalized =>
                FromWindowNormalized(request),

            ComputerCoordinateSpaces.VirtualDesktopNormalized =>
                FromVirtualDesktopNormalized(request, screen),

            _ => throw new ToolExecutionInputException(
                $"Hệ tọa độ không được hỗ trợ: {space}.")
        };

        ValidateDesktopBounds(point.DesktopX, point.DesktopY, screen);

        var monitor = displays.GetMonitorAtPoint(
            point.DesktopX,
            point.DesktopY);

        return monitor is null
            ? point
            : point with
            {
                MonitorDevice = monitor.DeviceName,
                DpiX = monitor.DpiX,
                DpiY = monitor.DpiY,
                ScaleX = monitor.ScaleX,
                ScaleY = monitor.ScaleY
            };
    }

    private static ComputerCoordinatePoint FromImagePixel(
        ComputerCoordinateRequest request,
        DesktopScreenshotFrame frame)
    {
        if (request.ImageX < 0 ||
            request.ImageX >= frame.Width ||
            request.ImageY < 0 ||
            request.ImageY >= frame.Height)
            throw new ToolExecutionInputException(
                "Tọa độ pixel nằm ngoài ảnh desktop hiện tại.");

        return new(
            checked(frame.Left + request.ImageX),
            checked(frame.Top + request.ImageY),
            ComputerCoordinateSpaces.ImagePixel,
            $"ảnh {frame.Width}x{frame.Height} tại ({request.ImageX},{request.ImageY})");
    }

    private static ComputerCoordinatePoint FromImageNormalized(
        ComputerCoordinateRequest request,
        DesktopScreenshotFrame frame)
    {
        ValidateNormalized(request.NormalizedX, request.NormalizedY);

        var x = frame.Left + ScaleNormalized(
            request.NormalizedX,
            frame.Width);
        var y = frame.Top + ScaleNormalized(
            request.NormalizedY,
            frame.Height);

        return new(
            x,
            y,
            ComputerCoordinateSpaces.ImageNormalized,
            $"ảnh chuẩn hóa ({request.NormalizedX:0.0000},{request.NormalizedY:0.0000})");
    }

    private ComputerCoordinatePoint FromWindowNormalized(
        ComputerCoordinateRequest request)
    {
        ValidateNormalized(request.NormalizedX, request.NormalizedY);

        var id = (request.WindowId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id))
            throw new ToolExecutionInputException(
                "Hệ tọa độ cửa sổ cần windowId.");

        var window = computer.GetWindows(50).Windows
            .FirstOrDefault(item =>
                item.WindowId.Equals(
                    id,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new ToolExecutionInputException(
                "Cửa sổ dùng làm hệ tọa độ không còn tồn tại.");

        if (window.Width <= 0 || window.Height <= 0)
            throw new ToolExecutionInputException(
                "Cửa sổ dùng làm hệ tọa độ không có kích thước hợp lệ.");

        var x = window.Left + ScaleNormalized(
            request.NormalizedX,
            window.Width);
        var y = window.Top + ScaleNormalized(
            request.NormalizedY,
            window.Height);

        return new(
            x,
            y,
            ComputerCoordinateSpaces.WindowNormalized,
            $"cửa sổ {window.WindowId} “{window.Title}” tại ({request.NormalizedX:0.0000},{request.NormalizedY:0.0000})");
    }

    private static ComputerCoordinatePoint FromVirtualDesktopNormalized(
        ComputerCoordinateRequest request,
        ComputerScreenInfo screen)
    {
        ValidateNormalized(request.NormalizedX, request.NormalizedY);

        var x = screen.VirtualLeft + ScaleNormalized(
            request.NormalizedX,
            screen.VirtualWidth);
        var y = screen.VirtualTop + ScaleNormalized(
            request.NormalizedY,
            screen.VirtualHeight);

        return new(
            x,
            y,
            ComputerCoordinateSpaces.VirtualDesktopNormalized,
            $"desktop ảo chuẩn hóa ({request.NormalizedX:0.0000},{request.NormalizedY:0.0000})");
    }

    private static int ScaleNormalized(
        double normalized,
        int size)
    {
        if (size <= 0)
            throw new ToolExecutionInputException(
                "Không thể đổi tọa độ vì kích thước vùng bằng 0.");

        return (int)Math.Round(
            normalized * Math.Max(0, size - 1),
            MidpointRounding.AwayFromZero);
    }

    private static void ValidateNormalized(
        double x,
        double y)
    {
        if (!double.IsFinite(x) ||
            !double.IsFinite(y) ||
            x < 0 ||
            x > 1 ||
            y < 0 ||
            y > 1)
            throw new ToolExecutionInputException(
                "Tọa độ chuẩn hóa phải nằm trong khoảng 0..1.");
    }

    private static void ValidateDesktopBounds(
        int x,
        int y,
        ComputerScreenInfo screen)
    {
        var right = checked(
            screen.VirtualLeft + screen.VirtualWidth);
        var bottom = checked(
            screen.VirtualTop + screen.VirtualHeight);

        if (x < screen.VirtualLeft ||
            x >= right ||
            y < screen.VirtualTop ||
            y >= bottom)
            throw new ToolExecutionInputException(
                $"Tọa độ sau chuyển đổi ({x},{y}) nằm ngoài desktop ảo hiện tại.");
    }
}
