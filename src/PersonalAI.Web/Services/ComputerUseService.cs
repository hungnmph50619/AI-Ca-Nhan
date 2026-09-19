using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerUseService
{
    ComputerUseStatusResponse GetStatus();

    ComputerScreenInfo GetScreenInfo();

    ComputerCursorPosition GetCursorPosition();

    ComputerWindowListResponse GetWindows(int limit = 30);

    ComputerWindowInfo? GetActiveWindow();

    ComputerActionResponse FocusWindow(string windowId);

    ComputerActionResponse MoveCursor(int x, int y);

    ComputerActionResponse ClickLeft(string windowId, int x, int y);
}

public sealed class WindowsComputerUseService(
    ComputerControlGate control) : IComputerUseService
{
    public const int MaximumWindows = 50;

    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const int SmCMonitors = 80;
    private const uint InputMouse = 0;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;

    public ComputerUseStatusResponse GetStatus()
    {
        var windows = OperatingSystem.IsWindows();
        var interactive = windows && Environment.UserInteractive;

        var capabilities = interactive
            ? new[]
            {
                ComputerUseCapabilities.ScreenInfo,
                ComputerUseCapabilities.CursorPosition,
                ComputerUseCapabilities.WindowList,
                ComputerUseCapabilities.ActiveWindow,
                ComputerUseCapabilities.FocusWindow,
                ComputerUseCapabilities.MoveCursor,
                ComputerUseCapabilities.ClickLeft
            }
            : Array.Empty<string>();

        var limitations = new List<string>
        {
            "Không chụp ảnh màn hình trong v1.1.0.",
            "Chỉ hỗ trợ một lần nhấp trái có xác nhận và khóa mặc định; chưa có nhấp phải, nhấp đúp, kéo thả, cuộn, gõ phím hoặc nhập văn bản.",
            "Không mở ứng dụng, chạy shell hoặc thực thi lệnh hệ thống.",
            "Các hành động thay đổi focus/cursor phải đi qua Tool Framework và xác nhận.",
            "Điều khiển được khóa lúc khởi động; phải cho phép thủ công. Nút dừng chỉ chặn các lệnh mới qua dịch vụ, không phải phím dừng toàn hệ thống.",
            "Nhấp chuột có thể kích hoạt hành động trong ứng dụng khác; chỉ thử trên cửa sổ thử nghiệm không chứa dữ liệu quan trọng."
        };

        if (!windows)
        {
            limitations.Insert(
                0,
                "Computer Use v1.1.0 hiện chỉ có backend điều khiển cho Windows.");
        }
        else if (!interactive)
        {
            limitations.Insert(
                0,
                "Tiến trình hiện không chạy trong interactive Windows session.");
        }

        return new ComputerUseStatusResponse(
            PersonalAiRelease.Version,
            RuntimeInformation.OSDescription,
            windows,
            interactive,
            capabilities,
            limitations,
            DesktopActionsPaused: control.Paused);
    }

    public ComputerScreenInfo GetScreenInfo()
    {
        EnsureAvailable();

        return new ComputerScreenInfo(
            GetSystemMetrics(SmCxScreen),
            GetSystemMetrics(SmCyScreen),
            GetSystemMetrics(SmXVirtualScreen),
            GetSystemMetrics(SmYVirtualScreen),
            GetSystemMetrics(SmCxVirtualScreen),
            GetSystemMetrics(SmCyVirtualScreen),
            Math.Max(1, GetSystemMetrics(SmCMonitors)));
    }

    public ComputerCursorPosition GetCursorPosition()
    {
        EnsureAvailable();

        if (!GetCursorPos(out var point))
        {
            throw new ToolExecutionInputException(
                "Windows không trả được vị trí con trỏ hiện tại.");
        }

        return new ComputerCursorPosition(
            point.X,
            point.Y);
    }

    public ComputerWindowListResponse GetWindows(int limit = 30)
    {
        EnsureAvailable();

        var safeLimit = Math.Clamp(
            limit,
            1,
            MaximumWindows);
        var foreground = GetForegroundWindow();
        var windows = new List<ComputerWindowInfo>();

        EnumWindows((handle, _) =>
        {
            if (windows.Count >= safeLimit)
            {
                return false;
            }

            if (!IsWindowVisible(handle))
            {
                return true;
            }

            var title = GetWindowTitle(handle);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            windows.Add(BuildWindowInfo(
                handle,
                title,
                handle == foreground));
            return true;
        }, IntPtr.Zero);

        return new ComputerWindowListResponse(
            windows.Count,
            windows);
    }

    public ComputerWindowInfo? GetActiveWindow()
    {
        EnsureAvailable();

        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero
            || !IsWindow(handle)
            || !IsWindowVisible(handle))
        {
            return null;
        }

        var title = GetWindowTitle(handle);
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return BuildWindowInfo(
            handle,
            title,
            true);
    }

    public ComputerActionResponse FocusWindow(
        string windowId) =>
        control.RunAllowed(() => FocusWindowCore(windowId));

    private ComputerActionResponse FocusWindowCore(
        string windowId)
    {
        EnsureAvailable();

        var handle = ParseWindowId(windowId);
        if (!IsWindow(handle)
            || !IsWindowVisible(handle))
        {
            throw new ToolExecutionInputException(
                "Cửa sổ không còn tồn tại hoặc không còn hiển thị.");
        }

        var title = GetWindowTitle(handle);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ToolExecutionInputException(
                "Không thể chuyển focus tới cửa sổ không có tiêu đề hiển thị.");
        }

        if (GetForegroundWindow() == handle)
        {
            return new ComputerActionResponse(
                ComputerUseCapabilities.FocusWindow,
                false,
                "Cửa sổ đã ở foreground.");
        }

        if (!SetForegroundWindow(handle))
        {
            throw new ToolExecutionInputException(
                "Windows đã từ chối chuyển focus tới cửa sổ này. Hãy kích hoạt cửa sổ thủ công rồi thử lại.");
        }

        return new ComputerActionResponse(
            ComputerUseCapabilities.FocusWindow,
            true,
            $"Đã yêu cầu Windows chuyển focus tới: {LimitInline(title, 160)}");
    }

    public ComputerActionResponse MoveCursor(
        int x,
        int y) =>
        control.RunAllowed(() => MoveCursorCore(x, y));

    private ComputerActionResponse MoveCursorCore(
        int x,
        int y)
    {
        EnsureAvailable();

        var screen = GetScreenInfo();
        var rightExclusive = checked(screen.VirtualLeft + screen.VirtualWidth);
        var bottomExclusive = checked(screen.VirtualTop + screen.VirtualHeight);

        if (x < screen.VirtualLeft
            || x >= rightExclusive
            || y < screen.VirtualTop
            || y >= bottomExclusive)
        {
            throw new ToolExecutionInputException(
                $"Tọa độ nằm ngoài desktop ảo hiện tại: X {screen.VirtualLeft}..{rightExclusive - 1}, Y {screen.VirtualTop}..{bottomExclusive - 1}.");
        }

        var current = GetCursorPosition();
        if (current.X == x && current.Y == y)
        {
            return new ComputerActionResponse(
                ComputerUseCapabilities.MoveCursor,
                false,
                "Con trỏ đã ở đúng tọa độ yêu cầu.");
        }

        if (!SetCursorPos(x, y))
        {
            throw new ToolExecutionInputException(
                "Windows không cho phép di chuyển con trỏ tới tọa độ yêu cầu.");
        }

        return new ComputerActionResponse(
            ComputerUseCapabilities.MoveCursor,
            true,
            $"Đã di chuyển con trỏ tới ({x}, {y}).");
    }

    public ComputerActionResponse ClickLeft(
        string windowId,
        int x,
        int y) =>
        control.RunAllowed(() => ClickLeftCore(windowId, x, y));

    private ComputerActionResponse ClickLeftCore(
        string windowId,
        int x,
        int y)
    {
        EnsureAvailable();
        var screen = GetScreenInfo();
        var right = checked(screen.VirtualLeft + screen.VirtualWidth);
        var bottom = checked(screen.VirtualTop + screen.VirtualHeight);
        if (x < screen.VirtualLeft || x >= right
            || y < screen.VirtualTop || y >= bottom)
            throw new ToolExecutionInputException("Tọa độ nhấp nằm ngoài màn hình hiện tại.");

        var target = ParseWindowId(windowId);
        if (!IsWindow(target) || !IsWindowVisible(target)
            || target != GetForegroundWindow()
            || !GetWindowRect(target, out var rect)
            || x < rect.Left || x >= rect.Right
            || y < rect.Top || y >= rect.Bottom)
            throw new ToolExecutionInputException(
                "Cửa sổ đích không còn ở phía trước hoặc tọa độ nằm ngoài cửa sổ. Hãy kiểm tra lại trước khi xác nhận.");

        // Không tự chọn cửa sổ, không di chuyển chuột tới nơi khác nếu cửa sổ đã đổi.
        if (!SetCursorPos(x, y) || GetForegroundWindow() != target)
            throw new ToolExecutionInputException(
                "Không thể xác nhận vị trí con trỏ và cửa sổ đích trước khi nhấp.");

        var inputs = new[]
        {
            new MouseInputEvent { Type = InputMouse,
                Mouse = new MouseInputData { Flags = MouseLeftDown } },
            new MouseInputEvent { Type = InputMouse,
                Mouse = new MouseInputData { Flags = MouseLeftUp } }
        };
        var count = SendInput((uint)inputs.Length, inputs,
            Marshal.SizeOf<MouseInputEvent>());
        if (count != (uint)inputs.Length)
        {
            // Nếu Windows chỉ phát được sự kiện nhấn, thử nhả ngay để tránh giữ nút.
            var release = new[]
            {
                new MouseInputEvent { Type = InputMouse,
                    Mouse = new MouseInputData { Flags = MouseLeftUp } }
            };
            _ = SendInput(1, release, Marshal.SizeOf<MouseInputEvent>());
            throw new ToolExecutionInputException(
                "Windows không xác nhận đủ sự kiện nhấp và nhả chuột.");
        }

        return new ComputerActionResponse(
            ComputerUseCapabilities.ClickLeft,
            true,
            "Đã gửi một lần nhấp chuột trái tại tọa độ đã xác nhận.");
    }

    private static ComputerWindowInfo BuildWindowInfo(
        IntPtr handle,
        string title,
        bool foreground)
    {
        _ = GetWindowThreadProcessId(
            handle,
            out var processId);

        string? processName = null;
        if (processId > 0)
        {
            try
            {
                processName = Process
                    .GetProcessById(checked((int)processId))
                    .ProcessName;
            }
            catch
            {
                processName = null;
            }
        }

        var left = 0;
        var top = 0;
        var width = 0;
        var height = 0;
        if (GetWindowRect(handle, out var rect))
        {
            left = rect.Left;
            top = rect.Top;
            width = Math.Max(0, rect.Right - rect.Left);
            height = Math.Max(0, rect.Bottom - rect.Top);
        }

        return new ComputerWindowInfo(
            FormatWindowId(handle),
            LimitInline(title, 240),
            LimitNullable(processName, 120),
            processId is > 0 and <= int.MaxValue
                ? (int)processId
                : null,
            foreground,
            left,
            top,
            width,
            height);
    }

    private static string GetWindowTitle(
        IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(
            Math.Min(length + 1, 2048));
        _ = GetWindowText(
            handle,
            buffer,
            buffer.Capacity);
        return buffer
            .ToString()
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

    private static IntPtr ParseWindowId(
        string windowId)
    {
        var value = (windowId ?? string.Empty).Trim();
        if (value.StartsWith(
            "0x",
            StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        if (value.Length == 0
            || !ulong.TryParse(
                value,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var raw)
            || raw == 0)
        {
            throw new ToolExecutionInputException(
                "Window ID không hợp lệ.");
        }

        return new IntPtr(
            unchecked((long)raw));
    }

    private static string FormatWindowId(
        IntPtr handle) =>
        $"0x{unchecked((ulong)handle.ToInt64()):X}";

    private static string LimitInline(
        string value,
        int maximum)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= maximum
            ? normalized
            : normalized[..Math.Max(0, maximum - 1)] + "…";
    }

    private static string? LimitNullable(
        string? value,
        int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximum
            ? normalized
            : normalized[..maximum];
    }

    private static void EnsureAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new ToolExecutionInputException(
                "Computer Use v1.1.0 hiện chỉ hỗ trợ Windows.");
        }

        if (!Environment.UserInteractive)
        {
            throw new ToolExecutionInputException(
                "Computer Use cần chạy trong interactive Windows session.");
        }
    }

    private delegate bool EnumWindowsProc(
        IntPtr hWnd,
        IntPtr lParam);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputEvent
    {
        public uint Type;
        public MouseInputData Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint numberOfInputs,
        [In] MouseInputEvent[] inputs,
        int inputSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsProc callback,
        IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(
        IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(
        IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(
        IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr hWnd,
        StringBuilder text,
        int maximumCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(
        IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr hWnd,
        out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr hWnd,
        out Rect rect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(
        int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(
        out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(
        int x,
        int y);
}
