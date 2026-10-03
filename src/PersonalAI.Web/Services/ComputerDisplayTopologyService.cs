using System.Runtime.InteropServices;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerDisplayTopologyService
{
    ComputerDisplayTopologyResponse GetTopology();

    ComputerMonitorInfo? GetMonitorAtPoint(
        int x,
        int y);
}

public sealed class WindowsComputerDisplayTopologyService
    : IComputerDisplayTopologyService
{
    private const uint MonitorInfoPrimary = 0x00000001;
    private const int MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;

    public ComputerDisplayTopologyResponse GetTopology()
    {
        if (!OperatingSystem.IsWindows())
            return new(0, Array.Empty<ComputerMonitorInfo>());

        var monitors = new List<ComputerMonitorInfo>();

        _ = EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitor, _, _, _) =>
            {
                if (TryBuildMonitorInfo(
                        monitor,
                        out var info))
                    monitors.Add(info);

                return true;
            },
            IntPtr.Zero);

        return new(
            monitors.Count,
            monitors
                .OrderByDescending(item => item.Primary)
                .ThenBy(item => item.Left)
                .ThenBy(item => item.Top)
                .ToArray());
    }

    public ComputerMonitorInfo? GetMonitorAtPoint(
        int x,
        int y)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var point = new Point
        {
            X = x,
            Y = y
        };

        var handle = MonitorFromPoint(
            point,
            MonitorDefaultToNearest);

        return handle == IntPtr.Zero
            ? null
            : TryBuildMonitorInfo(handle, out var info)
                ? info
                : null;
    }

    private static bool TryBuildMonitorInfo(
        IntPtr monitor,
        out ComputerMonitorInfo info)
    {
        var native = new MonitorInfoEx
        {
            Size = Marshal.SizeOf<MonitorInfoEx>(),
            DeviceName = string.Empty
        };

        if (!GetMonitorInfo(
                monitor,
                ref native))
        {
            info = default!;
            return false;
        }

        var dpiX = 96u;
        var dpiY = 96u;

        try
        {
            var result = GetDpiForMonitor(
                monitor,
                EffectiveDpi,
                out var monitorDpiX,
                out var monitorDpiY);

            if (result == 0 &&
                monitorDpiX > 0 &&
                monitorDpiY > 0)
            {
                dpiX = monitorDpiX;
                dpiY = monitorDpiY;
            }
        }
        catch (DllNotFoundException)
        {
            // Windows cũ hơn không có Shcore.dll.
        }
        catch (EntryPointNotFoundException)
        {
            // GetDpiForMonitor không khả dụng; dùng 96 DPI.
        }

        var width = Math.Max(
            0,
            native.Monitor.Right - native.Monitor.Left);
        var height = Math.Max(
            0,
            native.Monitor.Bottom - native.Monitor.Top);
        var workWidth = Math.Max(
            0,
            native.Work.Right - native.Work.Left);
        var workHeight = Math.Max(
            0,
            native.Work.Bottom - native.Work.Top);

        info = new ComputerMonitorInfo(
            string.IsNullOrWhiteSpace(native.DeviceName)
                ? "unknown-monitor"
                : native.DeviceName.Trim(),
            (native.Flags & MonitorInfoPrimary) != 0,
            native.Monitor.Left,
            native.Monitor.Top,
            width,
            height,
            native.Work.Left,
            native.Work.Top,
            workWidth,
            workHeight,
            dpiX,
            dpiY,
            dpiX / 96.0,
            dpiY / 96.0);

        return true;
    }

    private delegate bool MonitorEnumProc(
        IntPtr monitor,
        IntPtr hdc,
        IntPtr rect,
        IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 32)]
        public string DeviceName;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr clipRect,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr monitor,
        ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(
        Point point,
        int flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);
}
