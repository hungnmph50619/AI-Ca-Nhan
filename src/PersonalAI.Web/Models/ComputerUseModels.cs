namespace PersonalAI.Web.Models;

public static class ComputerUseCapabilities
{
    public const string ScreenInfo = "screen-info";
    public const string CursorPosition = "cursor-position";
    public const string WindowList = "window-list";
    public const string ActiveWindow = "active-window";
    public const string FocusWindow = "focus-window";
    public const string MoveCursor = "move-cursor";
}

public sealed record ComputerUseStatusResponse(
    string Version,
    string Platform,
    bool Supported,
    bool InteractiveSession,
    IReadOnlyList<string> AvailableCapabilities,
    IReadOnlyList<string> Limitations);

public sealed record ComputerScreenInfo(
    int PrimaryWidth,
    int PrimaryHeight,
    int VirtualLeft,
    int VirtualTop,
    int VirtualWidth,
    int VirtualHeight,
    int MonitorCount);

public sealed record ComputerCursorPosition(
    int X,
    int Y);

public sealed record ComputerWindowInfo(
    string WindowId,
    string Title,
    string? ProcessName,
    int? ProcessId,
    bool IsForeground,
    int Left,
    int Top,
    int Width,
    int Height);

public sealed record ComputerWindowListResponse(
    int Count,
    IReadOnlyList<ComputerWindowInfo> Windows);

public sealed record ComputerActionResponse(
    string Action,
    bool Applied,
    string Detail);
