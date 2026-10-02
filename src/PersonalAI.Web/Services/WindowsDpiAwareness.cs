using System.Runtime.InteropServices;

namespace PersonalAI.Web.Services;

public static class WindowsDpiAwareness
{
    private static int _initialized;

    public static void EnsurePerMonitorAware()
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        try
        {
            _ = SetProcessDpiAwarenessContext(
                new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        }
        catch
        {
            // Best effort. Existing Windows behavior remains available.
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(
        IntPtr dpiContext);
}
