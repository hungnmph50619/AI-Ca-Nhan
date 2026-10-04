namespace PersonalAI.Web.Models;

public static class ComputerCoordinateSpaces
{
    public const string ImagePixel = "image-pixel";
    public const string ImageNormalized = "image-normalized";
    public const string WindowNormalized = "window-normalized";
    public const string MonitorNormalized = "monitor-normalized";
    public const string VirtualDesktopNormalized = "virtual-desktop-normalized";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [
                ImagePixel,
                ImageNormalized,
                WindowNormalized,
                MonitorNormalized,
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
    string WindowId,
    string MonitorDevice = "");

public sealed record ComputerCoordinatePoint(
    int DesktopX,
    int DesktopY,
    string Space,
    string SourceDescription,
    string? MonitorDevice = null,
    uint DpiX = 96,
    uint DpiY = 96,
    double ScaleX = 1.0,
    double ScaleY = 1.0,
    bool DpiCalibrationVerified = false,
    string? DpiCalibrationDetail = null,
    double CanonicalX = 0.0,
    double CanonicalY = 0.0,
    double MonitorNormalizedX = 0.0,
    double MonitorNormalizedY = 0.0);


public sealed record ComputerCanonicalInteractionPoint(
    double X,
    double Y,
    int PhysicalX,
    int PhysicalY,
    string SourceSpace,
    string SourceDescription,
    string? WindowId,
    string? MonitorDevice,
    double MonitorX,
    double MonitorY,
    uint DpiX,
    uint DpiY,
    double ScaleX,
    double ScaleY,
    bool DpiCalibrationVerified,
    string? DpiCalibrationDetail);

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


public sealed record ComputerTargetRegion(
    string Space,
    string WindowId,
    int Left,
    int Top,
    int Width,
    int Height,
    double NormalizedLeft,
    double NormalizedTop,
    double NormalizedWidth,
    double NormalizedHeight);

public sealed record ComputerSafeTargetPoint(
    ComputerCoordinatePoint Point,
    bool UsedBoundingBox,
    string Detail);


public sealed record ComputerDpiAwarenessStatus(
    bool Windows,
    bool InitializationAttempted,
    bool SetContextSucceeded,
    int SetContextWin32Error,
    string Awareness,
    bool PerMonitorAware,
    bool PerMonitorAwareV2,
    bool PhysicalPixelCoordinatesExpected,
    string Detail);

public sealed record ComputerDpiCalibrationResponse(
    ComputerDpiAwarenessStatus Awareness,
    int MonitorCount,
    bool TopologyValid,
    IReadOnlyList<string> Warnings);
