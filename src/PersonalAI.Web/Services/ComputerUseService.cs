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

    ComputerWindowInfo? GetWindowAtPoint(int x, int y);

    ComputerActionResponse FocusWindow(string windowId);

    ComputerActionResponse FocusWindowByQuery(string query);

    ComputerActionResponse MinimizeWindow(string windowId);

    ComputerActionResponse MaximizeWindow(string windowId);

    ComputerActionResponse RestoreWindow(string windowId);

    ComputerActionResponse MoveCursor(int x, int y);

    ComputerActionResponse SmoothMoveCursor(
        int x,
        int y,
        int durationMs = 320);

    ComputerActionResponse ClickLeft(string windowId, int x, int y);

    ComputerActionResponse ClickRight(string windowId, int x, int y);

    ComputerActionResponse DoubleClickLeft(string windowId, int x, int y);

    ComputerActionResponse Scroll(string windowId, int x, int y, int delta);

    ComputerActionResponse DragLeft(
        string windowId,
        int startX,
        int startY,
        int endX,
        int endY,
        int durationMs = 500);

    ComputerActionResponse TypeNotepadText(string windowId, string text);

    ComputerActionResponse TypeText(string windowId, string text);

    ComputerActionResponse PressKey(string windowId, string key);

    ComputerActionResponse PressHotkey(
        string windowId,
        IReadOnlyList<string> keys);

    ComputerActionResponse OpenDefaultBrowser(string? url = null);
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
    public const int MaximumGenericTextLength = 1000;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseWheel = 0x0800;
    private const uint GaRoot = 2;

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
                ComputerUseCapabilities.FocusWindowByQuery,
                ComputerUseCapabilities.MinimizeWindow,
                ComputerUseCapabilities.MaximizeWindow,
                ComputerUseCapabilities.RestoreWindow,
                ComputerUseCapabilities.MoveCursor,
                ComputerUseCapabilities.SmoothMoveCursor,
                ComputerUseCapabilities.ClickLeft,
                ComputerUseCapabilities.ClickRight,
                ComputerUseCapabilities.DoubleClickLeft,
                ComputerUseCapabilities.Scroll,
                ComputerUseCapabilities.DragLeft,
                ComputerUseCapabilities.TypeNotepadText,
                ComputerUseCapabilities.TypeText,
                ComputerUseCapabilities.PressKey,
                ComputerUseCapabilities.PressHotkey,
                ComputerUseCapabilities.OpenDefaultBrowser
            }
            : Array.Empty<string>();

        var limitations = new List<string>
        {
            "Không chụp ảnh màn hình trong v1.1.0.",
            "Human Input Foundation hỗ trợ focus/minimize/maximize/restore, di chuột nhìn thấy được, click trái/phải/đúp, cuộn, kéo-thả, gõ văn bản và phím/hotkey có xác nhận.",
            "Không chạy shell hoặc thực thi lệnh hệ thống tùy ý. Chỉ cho phép mở trình duyệt mặc định của Windows tới URL HTTP/HTTPS đã kiểm tra.",
            "Các hành động thay đổi focus/cursor phải đi qua Tool Framework và xác nhận.",
            "Điều khiển được khóa lúc khởi động; phải cho phép thủ công. Nút dừng chỉ chặn các lệnh mới qua dịch vụ, không phải phím dừng toàn hệ thống.",
            "Nhấp chuột có thể kích hoạt hành động trong ứng dụng khác; chỉ thử trên cửa sổ thử nghiệm không chứa dữ liệu quan trọng.",
            "Nhập bàn phím tổng quát luôn cần xác nhận và quyền nhạy cảm; không dùng để nhập mật khẩu, mã xác thực hoặc bí mật. Nội dung có thể bị ứng dụng đích lưu lại.",
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
            var isForeground = handle == foreground;
            if (string.IsNullOrWhiteSpace(title) &&
                !isForeground)
            {
                return true;
            }

            windows.Add(BuildWindowInfo(
                handle,
                GetWindowDisplayTitle(handle, title),
                isForeground));
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

        return BuildWindowInfo(
            handle,
            GetWindowDisplayTitle(handle, title),
            true);
    }

    public ComputerWindowInfo? GetWindowAtPoint(
        int x,
        int y)
    {
        EnsureAvailable();

        var root = GetRootWindowAtPoint(x, y);
        if (root == IntPtr.Zero ||
            !IsWindow(root) ||
            !IsWindowVisible(root))
        {
            return null;
        }

        var foreground = GetForegroundWindow();
        var title = GetWindowTitle(root);

        return BuildWindowInfo(
            root,
            GetWindowDisplayTitle(root, title),
            root == foreground);
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

    public ComputerActionResponse FocusWindowByQuery(
        string query) =>
        control.RunAllowed(() =>
        {
            EnsureAvailable();

            var normalized = (query ?? string.Empty).Trim();
            if (normalized.Length is < 2 or > 120)
                throw new ToolExecutionInputException(
                    "Tên cửa sổ cần tìm phải từ 2 đến 120 ký tự.");

            var candidates = GetWindows(MaximumWindows).Windows
                .Where(window =>
                    !string.IsNullOrWhiteSpace(window.Title))
                .Select(window => new
                {
                    Window = window,
                    Score = ScoreWindowMatch(window, normalized)
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Window.IsForeground)
                .ToArray();

            if (candidates.Length == 0)
                throw new ToolExecutionInputException(
                    $"Không tìm thấy cửa sổ phù hợp với “{normalized}”.");

            var selected = candidates[0].Window;
            var result = FocusWindowCore(selected.WindowId);
            return result with
            {
                Action = ComputerUseCapabilities.FocusWindowByQuery,
                Detail = $"Đã chuyển sang cửa sổ “{selected.Title}” ({selected.ProcessName ?? "không rõ tiến trình"})."
            };
        });

    private static int ScoreWindowMatch(
        ComputerWindowInfo window,
        string query)
    {
        var title = window.Title ?? string.Empty;
        var process = window.ProcessName ?? string.Empty;

        if (title.Equals(query, StringComparison.OrdinalIgnoreCase))
            return 100;
        if (process.Equals(query, StringComparison.OrdinalIgnoreCase))
            return 95;
        if (title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 90;
        if (process.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 85;
        if (title.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 80;
        if (process.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 75;

        var compact = string.Join(
            " ",
            query.Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries));

        return compact.Length > 1 &&
               title.Contains(compact, StringComparison.OrdinalIgnoreCase)
            ? 70
            : 0;
    }

    public ComputerActionResponse MinimizeWindow(
        string windowId) =>
        control.RunAllowed(() => MinimizeWindowCore(windowId));

    private ComputerActionResponse MinimizeWindowCore(
        string windowId)
    {
        EnsureAvailable();

        var handle = ParseWindowId(windowId);
        if (!IsWindow(handle) || !IsWindowVisible(handle))
            throw new ToolExecutionInputException(
                "Cửa sổ cần thu nhỏ không còn tồn tại hoặc không hiển thị.");

        var title = GetWindowTitle(handle);
        if (string.IsNullOrWhiteSpace(title))
            throw new ToolExecutionInputException(
                "Không thể thu nhỏ cửa sổ không có tiêu đề.");

        _ = ShowWindow(handle, 6); // SW_MINIMIZE
        Thread.Sleep(180);

        return new ComputerActionResponse(
            ComputerUseCapabilities.MinimizeWindow,
            true,
            $"Đã thu nhỏ cửa sổ: {LimitInline(title, 160)}");
    }

    public ComputerActionResponse MaximizeWindow(
        string windowId) =>
        control.RunAllowed(() =>
            SetWindowShowState(
                windowId,
                3,
                ComputerUseCapabilities.MaximizeWindow,
                "phóng to"));

    public ComputerActionResponse RestoreWindow(
        string windowId) =>
        control.RunAllowed(() =>
            SetWindowShowState(
                windowId,
                9,
                ComputerUseCapabilities.RestoreWindow,
                "khôi phục"));

    private ComputerActionResponse SetWindowShowState(
        string windowId,
        int showCommand,
        string capability,
        string actionLabel)
    {
        EnsureAvailable();

        var handle = ParseWindowId(windowId);
        if (!IsWindow(handle) || !IsWindowVisible(handle))
            throw new ToolExecutionInputException(
                "Cửa sổ không còn tồn tại hoặc không hiển thị.");

        var title = GetWindowTitle(handle);
        if (string.IsNullOrWhiteSpace(title))
            throw new ToolExecutionInputException(
                "Không thể thay đổi trạng thái cửa sổ không có tiêu đề.");

        _ = ShowWindow(handle, showCommand);
        Thread.Sleep(180);

        return new ComputerActionResponse(
            capability,
            true,
            $"Đã {actionLabel} cửa sổ: {LimitInline(title, 160)}");
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

    public ComputerActionResponse SmoothMoveCursor(
        int x,
        int y,
        int durationMs = 320) =>
        control.RunAllowed(() =>
            SmoothMoveCursorCore(x, y, durationMs));

    private ComputerActionResponse SmoothMoveCursorCore(
        int x,
        int y,
        int durationMs)
    {
        EnsureAvailable();

        var screen = GetScreenInfo();
        var rightExclusive = checked(screen.VirtualLeft + screen.VirtualWidth);
        var bottomExclusive = checked(screen.VirtualTop + screen.VirtualHeight);
        if (x < screen.VirtualLeft ||
            x >= rightExclusive ||
            y < screen.VirtualTop ||
            y >= bottomExclusive)
            throw new ToolExecutionInputException(
                "Tọa độ di chuyển chuột nằm ngoài desktop ảo.");

        var start = GetCursorPosition();
        if (start.X == x && start.Y == y)
            return new ComputerActionResponse(
                ComputerUseCapabilities.SmoothMoveCursor,
                false,
                "Con trỏ đã ở mục tiêu.");

        var safeDuration = Math.Clamp(durationMs, 120, 1200);
        var steps = Math.Clamp(safeDuration / 24, 6, 40);
        var sleep = Math.Max(8, safeDuration / steps);

        for (var index = 1; index <= steps; index++)
        {
            var progress = index / (double)steps;
            var eased = progress * progress * (3d - 2d * progress);
            var nextX = (int)Math.Round(
                start.X + ((x - start.X) * eased));
            var nextY = (int)Math.Round(
                start.Y + ((y - start.Y) * eased));

            if (!SetCursorPos(nextX, nextY))
                throw new ToolExecutionInputException(
                    "Windows từ chối di chuyển con trỏ trong quá trình thao tác.");

            Thread.Sleep(sleep);
        }

        return new ComputerActionResponse(
            ComputerUseCapabilities.SmoothMoveCursor,
            true,
            $"Đã di chuyển con trỏ nhìn thấy được tới ({x}, {y}) trong khoảng {safeDuration} ms.");
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
        var topmostAtPoint = GetRootWindowAtPoint(x, y);
        if (!IsWindow(target) ||
            !IsWindowVisible(target) ||
            topmostAtPoint == IntPtr.Zero ||
            topmostAtPoint != target ||
            !GetWindowRect(target, out var rect) ||
            x < rect.Left || x >= rect.Right ||
            y < rect.Top || y >= rect.Bottom)
        {
            throw new ToolExecutionInputException(
                "Target dưới con trỏ đã thay đổi hoặc tọa độ không còn nằm trong cửa sổ đích. Không gửi click vào target cũ.");
        }

        // Click có thể chủ động chuyển foreground (ví dụ taskbar/background window).
        // An toàn dựa trên target topmost tại đúng điểm, không buộc target đã foreground trước click.
        if (!SetCursorPos(x, y) ||
            GetRootWindowAtPoint(x, y) != target)
        {
            throw new ToolExecutionInputException(
                "Không thể xác nhận target topmost tại vị trí con trỏ ngay trước khi nhấp.");
        }

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

    public ComputerActionResponse ClickRight(
        string windowId,
        int x,
        int y) =>
        control.RunAllowed(() =>
            ClickButtonCore(
                windowId,
                x,
                y,
                MouseRightDown,
                MouseRightUp,
                ComputerUseCapabilities.ClickRight,
                "nhấp chuột phải"));

    public ComputerActionResponse DoubleClickLeft(
        string windowId,
        int x,
        int y) =>
        control.RunAllowed(() =>
        {
            ValidatePointerTarget(windowId, x, y, out var target);
            if (!SetCursorPos(x, y) ||
                GetRootWindowAtPoint(x, y) != target)
                throw new ToolExecutionInputException(
                    "Không thể xác nhận target topmost trước khi nhấp đúp.");

            for (var click = 0; click < 2; click++)
            {
                SendMouseButton(MouseLeftDown, MouseLeftUp);
                if (click == 0)
                    Thread.Sleep(90);
            }

            return new ComputerActionResponse(
                ComputerUseCapabilities.DoubleClickLeft,
                true,
                "Đã gửi hai lần nhấp chuột trái liên tiếp tại tọa độ đã xác nhận.");
        });

    public ComputerActionResponse Scroll(
        string windowId,
        int x,
        int y,
        int delta) =>
        control.RunAllowed(() =>
        {
            ValidatePointerTarget(windowId, x, y, out var target);
            if (delta is < -2400 or > 2400 || delta == 0)
                throw new ToolExecutionInputException(
                    "Độ cuộn phải nằm trong khoảng -2400..2400 và khác 0.");

            if (!SetCursorPos(x, y) ||
                GetRootWindowAtPoint(x, y) != target)
                throw new ToolExecutionInputException(
                    "Không thể xác nhận target topmost trước khi cuộn.");

            var input = new[]
            {
                new NativeInputEvent
                {
                    Type = InputMouse,
                    Data = new NativeInputUnion
                    {
                        Mouse = new MouseInputData
                        {
                            MouseData = unchecked((uint)delta),
                            Flags = MouseWheel
                        }
                    }
                }
            };

            var sent = SendInput(
                1,
                input,
                Marshal.SizeOf<NativeInputEvent>());
            if (sent != 1)
                throw new ToolExecutionInputException(
                    "Windows không xác nhận sự kiện cuộn.");

            return new ComputerActionResponse(
                ComputerUseCapabilities.Scroll,
                true,
                $"Đã cuộn tại ({x}, {y}) với delta {delta}.");
        });

    public ComputerActionResponse DragLeft(
        string windowId,
        int startX,
        int startY,
        int endX,
        int endY,
        int durationMs = 500) =>
        control.RunAllowed(() =>
        {
            ValidatePointerTarget(
                windowId,
                startX,
                startY,
                out var target);
            ValidatePointerTarget(
                windowId,
                endX,
                endY,
                out var endTarget);
            if (target != endTarget)
                throw new ToolExecutionInputException(
                    "Điểm đầu và cuối kéo-thả phải nằm trong cùng cửa sổ foreground.");

            var safeDuration = Math.Clamp(durationMs, 180, 1800);
            var steps = Math.Clamp(safeDuration / 24, 8, 60);
            var sleep = Math.Max(8, safeDuration / steps);

            if (!SetCursorPos(startX, startY) ||
                GetRootWindowAtPoint(startX, startY) != target)
                throw new ToolExecutionInputException(
                    "Không thể xác nhận target topmost tại điểm bắt đầu kéo.");

            SendMouseDown(MouseLeftDown);
            try
            {
                for (var index = 1; index <= steps; index++)
                {
                    var p = index / (double)steps;
                    var eased = p * p * (3d - 2d * p);
                    var nextX = (int)Math.Round(
                        startX + ((endX - startX) * eased));
                    var nextY = (int)Math.Round(
                        startY + ((endY - startY) * eased));

                    if (GetRootWindowAtPoint(nextX, nextY) != target)
                        throw new ToolExecutionInputException(
                            "Target dưới con trỏ đã thay đổi trong lúc kéo-thả.");

                    if (!SetCursorPos(nextX, nextY))
                        throw new ToolExecutionInputException(
                            "Windows từ chối di chuyển chuột trong lúc kéo-thả.");

                    Thread.Sleep(sleep);
                }
            }
            finally
            {
                SendMouseUp(MouseLeftUp);
            }

            return new ComputerActionResponse(
                ComputerUseCapabilities.DragLeft,
                true,
                $"Đã kéo-thả từ ({startX}, {startY}) tới ({endX}, {endY}).");
        });

    private ComputerActionResponse ClickButtonCore(
        string windowId,
        int x,
        int y,
        uint downFlag,
        uint upFlag,
        string capability,
        string label)
    {
        ValidatePointerTarget(windowId, x, y, out var target);

        if (!SetCursorPos(x, y) || GetForegroundWindow() != target)
            throw new ToolExecutionInputException(
                $"Không thể xác nhận vị trí con trỏ và cửa sổ trước khi {label}.");

        SendMouseButton(downFlag, upFlag);

        return new ComputerActionResponse(
            capability,
            true,
            $"Đã gửi một lần {label} tại tọa độ đã xác nhận.");
    }

    private void ValidatePointerTarget(
        string windowId,
        int x,
        int y,
        out IntPtr target)
    {
        EnsureAvailable();
        var screen = GetScreenInfo();
        var right = checked(screen.VirtualLeft + screen.VirtualWidth);
        var bottom = checked(screen.VirtualTop + screen.VirtualHeight);
        if (x < screen.VirtualLeft || x >= right ||
            y < screen.VirtualTop || y >= bottom)
            throw new ToolExecutionInputException(
                "Tọa độ thao tác nằm ngoài desktop ảo.");

        target = ParseWindowId(windowId);
        var topmostAtPoint = GetRootWindowAtPoint(x, y);
        if (!IsWindow(target) ||
            !IsWindowVisible(target) ||
            topmostAtPoint == IntPtr.Zero ||
            topmostAtPoint != target ||
            !GetWindowRect(target, out var rect) ||
            x < rect.Left || x >= rect.Right ||
            y < rect.Top || y >= rect.Bottom)
        {
            throw new ToolExecutionInputException(
                "Target topmost dưới con trỏ đã thay đổi hoặc tọa độ nằm ngoài cửa sổ đích.");
        }
    }

    private static void SendMouseButton(
        uint downFlag,
        uint upFlag)
    {
        SendMouseDown(downFlag);
        SendMouseUp(upFlag);
    }

    private static void SendMouseDown(uint flag)
    {
        var input = new[]
        {
            new NativeInputEvent
            {
                Type = InputMouse,
                Data = new NativeInputUnion
                {
                    Mouse = new MouseInputData { Flags = flag }
                }
            }
        };

        if (SendInput(
                1,
                input,
                Marshal.SizeOf<NativeInputEvent>()) != 1)
            throw new ToolExecutionInputException(
                "Windows không xác nhận sự kiện nhấn chuột.");
    }

    private static void SendMouseUp(uint flag)
    {
        var input = new[]
        {
            new NativeInputEvent
            {
                Type = InputMouse,
                Data = new NativeInputUnion
                {
                    Mouse = new MouseInputData { Flags = flag }
                }
            }
        };

        if (SendInput(
                1,
                input,
                Marshal.SizeOf<NativeInputEvent>()) != 1)
            throw new ToolExecutionInputException(
                "Windows không xác nhận sự kiện nhả chuột.");
    }

    public ComputerActionResponse TypeText(
        string windowId,
        string text) =>
        control.RunAllowed(() =>
            TypeTextCore(
                windowId,
                text,
                MaximumGenericTextLength,
                ComputerUseCapabilities.TypeText));

    public ComputerActionResponse PressKey(
        string windowId,
        string key) =>
        control.RunAllowed(() =>
        {
            var target = ValidateKeyboardTarget(windowId);
            var virtualKey = ResolveVirtualKey(key);
            if (GetForegroundWindow() != target)
                throw new ToolExecutionInputException(
                    "Cửa sổ foreground đã thay đổi trước khi gửi phím.");

            SendVirtualKey(virtualKey, keyUp: false);
            SendVirtualKey(virtualKey, keyUp: true);

            return new ComputerActionResponse(
                ComputerUseCapabilities.PressKey,
                true,
                $"Đã nhấn phím {NormalizeKeyName(key)}.");
        });

    public ComputerActionResponse PressHotkey(
        string windowId,
        IReadOnlyList<string> keys) =>
        control.RunAllowed(() =>
        {
            if (keys is null || keys.Count is < 2 or > 4)
                throw new ToolExecutionInputException(
                    "Hotkey phải gồm từ 2 đến 4 phím.");

            var normalized = keys
                .Select(NormalizeKeyName)
                .ToArray();

            if (normalized.Distinct(
                    StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
                throw new ToolExecutionInputException(
                    "Hotkey không được chứa phím trùng.");

            var target = ValidateKeyboardTarget(windowId);
            var virtualKeys = normalized
                .Select(ResolveVirtualKey)
                .ToArray();

            if (GetForegroundWindow() != target)
                throw new ToolExecutionInputException(
                    "Cửa sổ foreground đã thay đổi trước khi gửi hotkey.");

            var pressed = new List<ushort>();
            try
            {
                foreach (var virtualKey in virtualKeys)
                {
                    SendVirtualKey(virtualKey, keyUp: false);
                    pressed.Add(virtualKey);
                    Thread.Sleep(18);
                }
            }
            finally
            {
                for (var index = pressed.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        SendVirtualKey(
                            pressed[index],
                            keyUp: true);
                    }
                    catch
                    {
                        // Best effort release; the emergency stop remains authoritative.
                    }
                }
            }

            return new ComputerActionResponse(
                ComputerUseCapabilities.PressHotkey,
                true,
                $"Đã gửi hotkey {string.Join("+", normalized)}.");
        });

    private ComputerActionResponse TypeTextCore(
        string windowId,
        string text,
        int maximumLength,
        string capability)
    {
        EnsureAvailable();

        if (string.IsNullOrEmpty(text) ||
            text.Length > maximumLength ||
            text.Any(character =>
                char.IsControl(character) ||
                char.IsSurrogate(character) ||
                char.GetUnicodeCategory(character) is
                    UnicodeCategory.LineSeparator or
                    UnicodeCategory.ParagraphSeparator or
                    UnicodeCategory.Format))
            throw new ToolExecutionInputException(
                $"Chỉ cho phép văn bản hiển thị từ 1 đến {maximumLength} ký tự; Enter/Tab và phím điều khiển phải dùng công cụ phím riêng.");

        var target = ValidateKeyboardTarget(windowId);
        if (GetForegroundWindow() != target)
            throw new ToolExecutionInputException(
                "Cửa sổ foreground đã thay đổi trước khi nhập văn bản.");

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
                        Scan = key,
                        Flags = KeyboardUnicode
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
                        Scan = key,
                        Flags = KeyboardUnicode | KeyboardKeyUp
                    }
                }
            };
        }

        var sent = SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeInputEvent>());
        if (sent != (uint)inputs.Length)
            throw new ToolExecutionInputException(
                "Windows chỉ tiếp nhận một phần lệnh nhập; không tự gửi lại để tránh nhập trùng.");

        return new ComputerActionResponse(
            capability,
            true,
            $"Đã nhập {text.Length} ký tự vào cửa sổ foreground đã xác nhận.");
    }

    private IntPtr ValidateKeyboardTarget(string windowId)
    {
        EnsureAvailable();

        var target = ParseWindowId(windowId);
        if (!IsWindow(target) ||
            !IsWindowVisible(target) ||
            GetForegroundWindow() != target)
            throw new ToolExecutionInputException(
                "Cửa sổ bàn phím đích không còn ở foreground.");

        return target;
    }

    private static string NormalizeKeyName(string key)
    {
        var normalized = (key ?? string.Empty)
            .Trim()
            .ToUpperInvariant()
            .Replace(" ", string.Empty);

        return normalized switch
        {
            "CONTROL" => "CTRL",
            "ESCAPE" => "ESC",
            "RETURN" => "ENTER",
            "WINDOWS" or "WINKEY" => "WIN",
            "PAGEUP" => "PGUP",
            "PAGEDOWN" => "PGDN",
            _ => normalized
        };
    }

    private static ushort ResolveVirtualKey(string key)
    {
        var normalized = NormalizeKeyName(key);
        if (normalized.Length == 1)
        {
            var character = normalized[0];
            if (character is >= 'A' and <= 'Z' ||
                character is >= '0' and <= '9')
                return character;
        }

        if (normalized.StartsWith('F') &&
            int.TryParse(
                normalized[1..],
                out var functionNumber) &&
            functionNumber is >= 1 and <= 12)
            return (ushort)(0x70 + functionNumber - 1);

        return normalized switch
        {
            "BACKSPACE" => 0x08,
            "TAB" => 0x09,
            "ENTER" => 0x0D,
            "SHIFT" => 0x10,
            "CTRL" => 0x11,
            "ALT" => 0x12,
            "PAUSE" => 0x13,
            "CAPSLOCK" => 0x14,
            "ESC" => 0x1B,
            "SPACE" => 0x20,
            "PGUP" => 0x21,
            "PGDN" => 0x22,
            "END" => 0x23,
            "HOME" => 0x24,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            "WIN" => 0x5B,
            _ => throw new ToolExecutionInputException(
                $"Phím không được hỗ trợ: {normalized}.")
        };
    }

    private static void SendVirtualKey(
        ushort virtualKey,
        bool keyUp)
    {
        var input = new[]
        {
            new NativeInputEvent
            {
                Type = InputKeyboard,
                Data = new NativeInputUnion
                {
                    Keyboard = new KeyboardInputData
                    {
                        VirtualKey = virtualKey,
                        Flags = keyUp
                            ? KeyboardKeyUp
                            : 0
                    }
                }
            }
        };

        if (SendInput(
                1,
                input,
                Marshal.SizeOf<NativeInputEvent>()) != 1)
            throw new ToolExecutionInputException(
                "Windows không xác nhận sự kiện bàn phím.");
    }

    public ComputerActionResponse OpenDefaultBrowser(string? url = null) =>
        control.RunAllowed(() => OpenDefaultBrowserCore(url));

    private ComputerActionResponse OpenDefaultBrowserCore(string? url)
    {
        EnsureAvailable();

        var target = string.IsNullOrWhiteSpace(url)
            ? "https://www.google.com/"
            : url.Trim();

        if (target.Length > 2048 ||
            !Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ToolExecutionInputException(
                "Chỉ được mở URL HTTP/HTTPS tuyệt đối, tối đa 2048 ký tự.");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true
            });

            if (process is null)
            {
                throw new ToolExecutionInputException(
                    "Windows không khởi chạy được trình duyệt mặc định.");
            }
        }
        catch (ToolExecutionInputException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            NotSupportedException)
        {
            throw new ToolExecutionInputException(
                "Không thể mở trình duyệt mặc định bằng Windows shell.");
        }

        return new ComputerActionResponse(
            ComputerUseCapabilities.OpenDefaultBrowser,
            true,
            $"Đã yêu cầu Windows mở trình duyệt mặc định tới {uri.Scheme}://{uri.Host}.");
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

    private static string GetWindowDisplayTitle(
        IntPtr handle,
        string? title = null)
    {
        var visibleTitle = string.IsNullOrWhiteSpace(title)
            ? GetWindowTitle(handle)
            : title.Trim();

        if (!string.IsNullOrWhiteSpace(visibleTitle))
            return visibleTitle;

        var className = GetWindowClassName(handle);
        if (!string.IsNullOrWhiteSpace(className))
            return $"[{className}]";

        return "[cửa sổ không tiêu đề]";
    }

    private static string GetWindowClassName(
        IntPtr handle)
    {
        var buffer = new StringBuilder(256);
        var length = GetClassName(
            handle,
            buffer,
            buffer.Capacity);

        return length > 0
            ? buffer.ToString().Trim()
            : string.Empty;
    }

    private static IntPtr GetRootWindowAtPoint(
        int x,
        int y)
    {
        var child = WindowFromPoint(
            new Point
            {
                X = x,
                Y = y
            });

        if (child == IntPtr.Zero)
            return IntPtr.Zero;

        var root = GetAncestor(
            child,
            GaRoot);

        return root != IntPtr.Zero
            ? root
            : child;
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
    private static extern IntPtr WindowFromPoint(
        Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(
        IntPtr hWnd,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr hWnd,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(
        IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(
        IntPtr hWnd,
        int command);

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
