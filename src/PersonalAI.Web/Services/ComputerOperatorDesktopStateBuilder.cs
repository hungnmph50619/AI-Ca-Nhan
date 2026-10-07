namespace PersonalAI.Web.Services;

public interface IComputerOperatorDesktopStateBuilder
{
    ComputerOperatorDesktopState Build(
        DesktopScreenshotFrame frame);
}

/// <summary>
/// Hợp nhất frame hiện tại với trạng thái cửa sổ và structured desktop
/// thành một snapshot quan sát duy nhất. Chỉ xây dựng observation evidence;
/// không plan, không quyết định action và không execute.
/// </summary>
public sealed class ComputerOperatorDesktopStateBuilder(
    IComputerUseService computer,
    IStructuredDesktopSnapshotService structuredDesktop)
    : IComputerOperatorDesktopStateBuilder
{
    public ComputerOperatorDesktopState Build(
        DesktopScreenshotFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var windows =
            computer.GetWindows(50).Windows;

        var active =
            windows.FirstOrDefault(window =>
                window.IsForeground)
            ?? computer.GetActiveWindow();

        StructuredDesktopSnapshot? structuredScene =
            null;

        if (active is not null &&
            !string.IsNullOrWhiteSpace(
                active.WindowId))
        {
            structuredScene =
                structuredDesktop.CaptureWindow(
                    active.WindowId,
                    maximumNodes: 240,
                    maximumDepth: 7);
        }

        var structuredGraph =
            UnifiedStructuredSceneGraphBuilder.Build(
                structuredScene,
                active,
                frame.Left,
                frame.Top,
                frame.Width,
                frame.Height);

        return new ComputerOperatorDesktopState(
            frame.CapturedAtUtc,
            active,
            windows,
            frame.Left,
            frame.Top,
            frame.Width,
            frame.Height,
            frame.CaptureScope,
            frame.WindowId,
            frame.WindowWasForeground,
            structuredScene,
            structuredGraph);
    }
}
