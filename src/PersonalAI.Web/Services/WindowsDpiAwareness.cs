using System.Runtime.InteropServices;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class WindowsDpiAwareness
{
    private static int _initialized;
    private static bool _setContextSucceeded;
    private static int _setContextWin32Error;

    private static readonly IntPtr PerMonitorAwareV2 =
        new(-4);

    public static void EnsurePerMonitorAware()
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (Interlocked.Exchange(
                ref _initialized,
                1) != 0)
            return;

        try
        {
            _setContextSucceeded =
                SetProcessDpiAwarenessContext(
                    PerMonitorAwareV2);

            if (!_setContextSucceeded)
                _setContextWin32Error =
                    Marshal.GetLastWin32Error();
        }
        catch
        {
            _setContextSucceeded = false;
            _setContextWin32Error = -1;
        }
    }

    public static ComputerDpiAwarenessStatus GetStatus()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new(
                false,
                Volatile.Read(ref _initialized) != 0,
                false,
                0,
                "không áp dụng",
                false,
                false,
                false,
                "DPI awareness chỉ áp dụng cho Windows.");
        }

        try
        {
            var context =
                GetThreadDpiAwarenessContext();
            var awareness =
                GetAwarenessFromDpiAwarenessContext(
                    context);
            var perMonitor =
                awareness == DpiAwarenessPerMonitor;
            var v2 =
                context != IntPtr.Zero &&
                AreDpiAwarenessContextsEqual(
                    context,
                    PerMonitorAwareV2);

            var label = awareness switch
            {
                DpiAwarenessUnaware => "unaware",
                DpiAwarenessSystem => "system-aware",
                DpiAwarenessPerMonitor => "per-monitor-aware",
                _ => "unknown"
            };

            return new(
                true,
                Volatile.Read(ref _initialized) != 0,
                _setContextSucceeded,
                _setContextWin32Error,
                label,
                perMonitor,
                v2,
                perMonitor,
                v2
                    ? "Tiến trình đang dùng Per-Monitor DPI Aware V2; screenshot, GetWindowRect và input có thể giữ cùng hệ pixel vật lý mà không scale thủ công lần hai."
                    : perMonitor
                        ? "Tiến trình đang per-monitor aware nhưng không xác nhận được V2."
                        : "Tiến trình chưa ở chế độ per-monitor aware; cần thận trọng khi quy đổi tọa độ giữa các màn hình có DPI khác nhau.");
        }
        catch (Exception exception) when (
            exception is
                DllNotFoundException or
                EntryPointNotFoundException)
        {
            return new(
                true,
                Volatile.Read(ref _initialized) != 0,
                _setContextSucceeded,
                _setContextWin32Error,
                "unknown",
                false,
                false,
                false,
                $"Không đọc được DPI awareness runtime: {exception.GetType().Name}.");
        }
    }

    private const int DpiAwarenessUnaware = 0;
    private const int DpiAwarenessSystem = 1;
    private const int DpiAwarenessPerMonitor = 2;

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(
        IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(
        IntPtr dpiContext);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(
        IntPtr dpiContextA,
        IntPtr dpiContextB);
}
