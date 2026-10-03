namespace PersonalAI.Web.Models;

public static class ComputerCoordinateSpaces
{
    public const string ImagePixel = "image-pixel";
    public const string ImageNormalized = "image-normalized";
    public const string WindowNormalized = "window-normalized";
    public const string VirtualDesktopNormalized = "virtual-desktop-normalized";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [
                ImagePixel,
                ImageNormalized,
                WindowNormalized,
                VirtualDesktopNormalized
            ],
            StringComparer.OrdinalIgnoreCase);
}

public sealed record ComputerCoordinateRequest(
    string Space,
    int ImageX,
    int ImageY,
    double NormalizedX,
    double NormalizedY,
    string WindowId);

public sealed record ComputerCoordinatePoint(
    int DesktopX,
    int DesktopY,
    string Space,
    string SourceDescription,
    string? MonitorDevice = null,
    uint DpiX = 96,
    uint DpiY = 96,
    double ScaleX = 1.0,
    double ScaleY = 1.0);


public sealed record ComputerMonitorInfo(
    string DeviceName,
    bool Primary,
    int Left,
    int Top,
    int Width,
    int Height,
    int WorkLeft,
    int WorkTop,
    int WorkWidth,
    int WorkHeight,
    uint DpiX,
    uint DpiY,
    double ScaleX,
    double ScaleY);

public sealed record ComputerDisplayTopologyResponse(
    int Count,
    IReadOnlyList<ComputerMonitorInfo> Monitors);
