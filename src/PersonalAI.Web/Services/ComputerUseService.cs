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

    ComputerActionResponse TypeNotepadText(string windowId, string text);
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
    private const uint InputKeyboard = 1;
    private const uint KeyboardUnicode = 0x0004;
    private const uint KeyboardKeyUp = 0x0002;
    public const int MaximumNotepadTextLength = 32;
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
                ComputerUseCapabilities.ClickLeft,
                ComputerUseCapabilities.TypeNotepadText
            }
            : Array.Empty<string>();

        var limitations = new List<string>
        {
            "Không chụp ảnh màn hình trong v1.1.0.",
            "Chỉ hỗ trợ nhấp trái từng lần và nhập một dòng tối đa 32 ký tự vào Notepad có xác nhận; chưa có nhấp phải, nhấp đúp, kéo thả, cuộn hoặc phím tắt.",
            "Không mở ứng dụng, chạy shell hoặc thực thi lệnh hệ thống.",
            "Các hành động thay đổi focus/cursor phải đi qua Tool Framework và xác nhận.",
            "Điều khiển được khóa lúc khởi động; phải cho phép thủ công. Nút dừng chỉ chặn các lệnh mới qua dịch vụ, không phải phím dừng toàn hệ thống.",
            "Nhấp chuột có thể kích hoạt hành động trong ứng dụng khác; chỉ thử trên cửa sổ thử nghiệm không chứa dữ liệu quan trọng.",
            "Nhập bàn phím chỉ dành cho cửa sổ Notepad đang hoạt động; không nhập mật khẩu, mã xác thực hoặc dữ liệu nhạy cảm. Nội dung có thể bị ứng dụng đích lưu lại.",
            "Mỗi lần bật chỉ có tối đa 60 giây và 5 thao tác, tính cả thao tác bị Windows từ chối.",
            "Nhấn Ctrl + Shift + F12 để khóa lại các thao tác máy tính khi phím dừng đã đăng ký; nếu phím bị ứng dụng khác sử dụng, ứng dụng không cho phép bật điều khiển."
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

        var session = control.GetStatus();
        return new ComputerUseStatusResponse(
            PersonalAiRelease.Version,
            RuntimeInformation.OSDescription,
            windows,
            interactive,
            capabilities,
            limitations,
            DesktopActionsPaused: session.Paused,
            DesktopSessionExpiresAt: session.ExpiresAt,
            DesktopRemainingActions: session.RemainingActions,
            StopHotkeyAvailable: session.StopHotkeyAvailable);
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
            new NativeInputEvent { Type = InputMouse,
                Data = new NativeInputUnion { Mouse = new MouseInputData { Flags = MouseLeftDown } } },
            new NativeInputEvent { Type = InputMouse,
                Data = new NativeInputUnion { Mouse = new MouseInputData { Flags = MouseLeftUp } } }
        };
        var count = SendInput((uint)inputs.Length, inputs,
            Marshal.SizeOf<NativeInputEvent>());
        if (count != (uint)inputs.Length)
        {
            // Nếu Windows chỉ phát được sự kiện nhấn, thử nhả ngay để tránh giữ nút.
            var release = new[]
            {
                new NativeInputEvent { Type = InputMouse,
                    Data = new NativeInputUnion { Mouse = new MouseInputData { Flags = MouseLeftUp } } }
            };
            _ = SendInput(1, release, Marshal.SizeOf<NativeInputEvent>());
            throw new ToolExecutionInputException(
                "Windows không xác nhận đủ sự kiện nhấp và nhả chuột.");
        }

        return new ComputerActionResponse(
            ComputerUseCapabilities.ClickLeft,
            true,
            "Đã gửi một lần nhấp chuột trái tại tọa độ đã xác nhận.");
    }

    public ComputerActionResponse TypeNotepadText(
        string windowId, string text) =>
        control.RunAllowed(() => TypeNotepadTextCore(windowId, text));

    private ComputerActionResponse TypeNotepadTextCore(
        string windowId, string text)
    {
        EnsureAvailable();
        if (string.IsNullOrEmpty(text)
            || text.Length > MaximumNotepadTextLength
            || text.Any(character => char.IsControl(character)
                || char.IsSurrogate(character)
                || char.GetUnicodeCategory(character) is
                    UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                        or UnicodeCategory.Format))
        {
            throw new ToolExecutionInputException(
                "Chỉ cho phép một dòng văn bản từ 1 đến 32 ký tự; không nhận phím điều khiển, xuống dòng, tab hoặc ký tự đặc biệt không hiển thị.");
        }

        var target = ParseWindowId(windowId);
        if (!IsWindow(target) || !IsWindowVisible(target)
            || GetForegroundWindow() != target)
        {
            throw new ToolExecutionInputException(
                "Cửa sổ Notepad đích không còn ở phía trước. Hãy chọn lại trước khi xác nhận.");
        }

        _ = GetWindowThreadProcessId(target, out var processId);
        if (processId is 0 or > int.MaxValue)
            throw new ToolExecutionInputException("Không xác định được cửa sổ Notepad đích.");

        string processName;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new ToolExecutionInputException(
                "Không xác minh được tiến trình Notepad đang hoạt động.");
        }

        if (!string.Equals(processName, "notepad",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolExecutionInputException(
                "Bản thử nghiệm chỉ cho phép nhập văn bản vào ứng dụng Notepad đang hoạt động.");
        }

        var inputs = new NativeInputEvent[text.Length * 2];
        for (var index = 0; index < text.Length; index++)
        {
            var key = (ushort)text[index];
            inputs[index * 2] = new NativeInputEvent
            {
                Type = InputKeyboard,
                Data = new NativeInputUnion
                {
                    Keyboard = new KeyboardInputData
                    {
                        Scan = key, Flags = KeyboardUnicode
                    }
                }
            };
            inputs[index * 2 + 1] = new NativeInputEvent
            {
                Type = InputKeyboard,
                Data = new NativeInputUnion
                {
                    Keyboard = new KeyboardInputData
                    {
                        Scan = key, Flags = KeyboardUnicode | KeyboardKeyUp
                    }
                }
            };
        }

        if (GetForegroundWindow() != target)
            throw new ToolExecutionInputException(
                "Cửa sổ đích đã thay đổi; đã hủy lệnh nhập văn bản.");

        var sent = SendInput((uint)inputs.Length, inputs,
            Marshal.SizeOf<NativeInputEvent>());
        if (sent != (uint)inputs.Length)
        {
            // Nếu một phần lệnh đã được gửi, không thử gửi lại văn bản
            // vì có thể gây nhập trùng hoặc nhập vào cửa sổ đã đổi.
            throw new ToolExecutionInputException(
                "Windows chỉ tiếp nhận một phần lệnh nhập; hãy kiểm tra nội dung Notepad trước khi thử lại.");
        }

        return new ComputerActionResponse(
            ComputerUseCapabilities.TypeNotepadText,
            true,
            $"Đã gửi {text.Length} ký tự tới Notepad đang hoạt động.");
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
    private struct NativeInputEvent
    {
        public uint Type;
        public NativeInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        public MouseInputData Mouse;

        [FieldOffset(0)]
        public KeyboardInputData Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
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
        [In] NativeInputEvent[] inputs,
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
