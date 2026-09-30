using System.Runtime.InteropServices;
using PersonalAI.Web.Services;

namespace PersonalAI.Web.Services;

/// <summary>
/// Phím dừng Windows Ctrl + Shift + F12, độc lập với trang web đang mở.
/// Đăng ký trên luồng riêng có hàng đợi thông điệp Windows.
/// Đây không phải cơ chế dừng khẩn cấp của toàn hệ điều hành.
/// </summary>
public sealed class WindowsStopHotkeyService(
    ComputerControlGate control,
    ILogger<WindowsStopHotkeyService> logger) : IHostedService
{
    private const int HotkeyId = 0x5041;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyF12 = 0x7B;
    private const uint WindowMessageHotkey = 0x0312;
    private const uint WindowMessageQuit = 0x0012;

    private Thread? _worker;
    private uint _threadId;
    private int _stopping;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        control.SetStopHotkeyAvailable(false);
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
        {
            logger.LogInformation(
                "Phím dừng máy tính không được đăng ký: không có phiên Windows tương tác.");
            return Task.CompletedTask;
        }

        _worker = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "PersonalAI-Phim-Dung-May-Tinh"
        };
        _worker.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _stopping, 1);
        control.SetStopHotkeyAvailable(false);
        var id = Volatile.Read(ref _threadId);
        if (id != 0)
        {
            _ = PostThreadMessage(id, WindowMessageQuit, IntPtr.Zero, IntPtr.Zero);
        }

        // Không chờ vô hạn trong quá trình tắt máy chủ.
        _worker?.Join(TimeSpan.FromSeconds(2));
        return Task.CompletedTask;
    }

    private void MessageLoop()
    {
        var registered = false;
        try
        {
            // Tạo hàng đợi thông điệp trước khi cho phép StopAsync gửi WM_QUIT.
            _ = PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            Volatile.Write(ref _threadId, GetCurrentThreadId());
            if (Volatile.Read(ref _stopping) != 0)
                return;

            registered = RegisterHotKey(
                IntPtr.Zero, HotkeyId,
                ModControl | ModShift | ModNoRepeat, VirtualKeyF12);
            if (!registered)
            {
                logger.LogWarning(
                    "Không đăng ký được Ctrl + Shift + F12 (Windows: {Error}). Điều khiển máy tính vẫn bị khóa.",
                    Marshal.GetLastWin32Error());
                return;
            }

            if (Volatile.Read(ref _stopping) != 0)
                return;

            control.SetStopHotkeyAvailable(true);
            logger.LogInformation("Đã đăng ký phím dừng Ctrl + Shift + F12.");

            while (Volatile.Read(ref _stopping) == 0)
            {
                var result = GetMessage(out var message, IntPtr.Zero, 0, 0);
                if (result <= 0)
                {
                    if (result == -1)
                        logger.LogWarning(
                            "Hàng đợi phím dừng gặp lỗi Windows {Error}; khóa điều khiển máy tính.",
                            Marshal.GetLastWin32Error());
                    break;
                }

                if (message.Message == WindowMessageHotkey
                    && message.WParam == new IntPtr(HotkeyId))
                {
                    control.Stop();
                    logger.LogWarning("Đã dừng điều khiển máy tính bằng phím tắt Windows.");
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Bộ nhận phím dừng không hoạt động; khóa điều khiển máy tính.");
        }
        finally
        {
            control.SetStopHotkeyAvailable(false);
            if (registered)
                _ = UnregisterHotKey(IntPtr.Zero, HotkeyId);
            Volatile.Write(ref _threadId, 0);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(
        out NativeMessage message,
        IntPtr window, uint minimum, uint maximum);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out NativeMessage message,
        IntPtr window, uint minimum, uint maximum, uint removal);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(
        uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
