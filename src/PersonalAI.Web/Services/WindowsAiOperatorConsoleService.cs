using System.Runtime.InteropServices;
using System.Text;

namespace PersonalAI.Web.Services;

public sealed class WindowsAiOperatorConsoleService(
    LeagueVisualProgressStore leagueProgress,
    ComputerOperatorProgressStore operatorProgress,
    ComputerOperatorExecutionControl execution,
    ComputerControlGate control,
    ILogger<WindowsAiOperatorConsoleService> logger)
    : IHostedService, IDisposable
{
    private const int ConsoleWidth = 620;
    private const int ConsoleHeight = 470;
    private const int Margin = 18;
    private const int ButtonHeight = 30;
    private const int ButtonWidth = 128;
    private const int Gap = 8;

    private const uint WsPopup = 0x80000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsChild = 0x40000000;
    private const uint SsLeft = 0x00000000;
    private const uint BsPushButton = 0x00000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x00000002;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const uint PmRemove = 0x0001;
    private const uint WmSetFont = 0x0030;
    private const uint WmCommand = 0x0111;
    private const uint WmCtlColorStatic = 0x0138;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const int DefaultGuiFont = 17;
    private const int BlackBrush = 4;
    private const int IdPause = 4101;
    private const int IdResume = 4102;
    private const int IdStop = 4103;

    private readonly object _sync = new();
    private readonly WindowProcedure _windowProcedure;
    private Thread? _thread;
    private CancellationTokenSource? _shutdown;
    private IntPtr _window;
    private IntPtr _text;
    private IntPtr _pauseButton;
    private IntPtr _resumeButton;
    private IntPtr _stopButton;
    private IntPtr _blackBrush;
    private DateTimeOffset _keepVisibleUntilUtc;
    private string _lastText = string.Empty;
    private string? _registeredClass;

    public WindowsAiOperatorConsoleService(
        LeagueVisualProgressStore leagueProgress,
        ComputerOperatorProgressStore operatorProgress,
        ComputerOperatorExecutionControl execution,
        ComputerControlGate control,
        ILogger<WindowsAiOperatorConsoleService> logger,
        bool _ = false)
        : this(leagueProgress, operatorProgress, execution, control, logger)
    {
    }

    // Primary-constructor fields are referenced through generated captures.
    // This explicit ctor overload is never selected by DI; it keeps the
    // delegate initialization straightforward across runtime versions.
    private WindowsAiOperatorConsoleService(
        LeagueVisualProgressStore leagueProgress,
        ComputerOperatorProgressStore operatorProgress,
        ComputerOperatorExecutionControl execution,
        ComputerControlGate control,
        ILogger<WindowsAiOperatorConsoleService> logger,
        int _unused)
        : this(leagueProgress, operatorProgress, execution, control, logger)
    {
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            return Task.CompletedTask;

        lock (_sync)
        {
            if (_thread is not null)
                return Task.CompletedTask;

            _shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _thread = new Thread(() => Run(_shutdown.Token))
            {
                IsBackground = true,
                Name = "PersonalAI-AI-Operator-Console"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Thread? thread;
        lock (_sync)
        {
            _shutdown?.Cancel();
            thread = _thread;
        }

        if (thread is not null && thread.IsAlive)
            thread.Join(TimeSpan.FromSeconds(2));

        return Task.CompletedTask;
    }

    private void Run(CancellationToken cancellationToken)
    {
        try
        {
            _registeredClass = "PersonalAI.OperatorConsole." + Environment.ProcessId;
            _windowProcedure = WindowProc;

            var instance = GetModuleHandle(null);
            _blackBrush = GetStockObject(BlackBrush);

            var windowClass = new WindowClassEx
            {
                Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
                Instance = instance,
                Background = _blackBrush,
                ClassName = _registeredClass
            };

            if (RegisterClassEx(ref windowClass) == 0)
            {
                logger.LogWarning(
                    "Không đăng ký được lớp cửa sổ AI Operator Console. Win32={Error}.",
                    Marshal.GetLastWin32Error());
                return;
            }

            _window = CreateWindowEx(
                WsExTopmost | WsExToolWindow | WsExNoActivate | WsExLayered,
                _registeredClass,
                "AI Operator Console",
                WsPopup | WsVisible,
                0,
                0,
                ConsoleWidth,
                ConsoleHeight,
                IntPtr.Zero,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);

            if (_window == IntPtr.Zero)
            {
                logger.LogWarning(
                    "Không tạo được AI Operator Console. Win32={Error}.",
                    Marshal.GetLastWin32Error());
                return;
            }

            _ = SetLayeredWindowAttributes(_window, 0, 244, LwaAlpha);
            _ = SetWindowDisplayAffinity(_window, 0x00000011); // WDA_EXCLUDEFROMCAPTURE

            var font = GetStockObject(DefaultGuiFont);
            _text = CreateWindowEx(
                0,
                "STATIC",
                "AI Operator Console đang chờ tác vụ…",
                WsChild | WsVisible | SsLeft,
                14,
                54,
                ConsoleWidth - 28,
                ConsoleHeight - 68,
                _window,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);

            _pauseButton = CreateButton(
                "TẠM DỪNG",
                14,
                14,
                IdPause,
                instance);
            _resumeButton = CreateButton(
                "TIẾP TỤC",
                14 + ButtonWidth + Gap,
                14,
                IdResume,
                instance);
            _stopButton = CreateButton(
                "DỪNG NGAY ■",
                14 + (ButtonWidth + Gap) * 2,
                14,
                IdStop,
                instance);

            foreach (var handle in new[] { _text, _pauseButton, _resumeButton, _stopButton })
            {
                if (handle != IntPtr.Zero && font != IntPtr.Zero)
                    _ = SendMessage(handle, WmSetFont, font, new IntPtr(1));
            }

            ShowWindow(_window, SwHide);

            while (!cancellationToken.IsCancellationRequested)
            {
                PumpMessages();

                var operatorSnapshot = operatorProgress.Get();
                var leagueSnapshot = leagueProgress.Get();
                var useOperator =
                    operatorSnapshot.Active ||
                    operatorSnapshot.Status is "completed" or "blocked" or "stopped" or "paused"
                    && operatorSnapshot.UpdatedAtUtc >= leagueSnapshot.UpdatedAtUtc;

                var selectedActive = useOperator
                    ? operatorSnapshot.Active
                    : leagueSnapshot.Active;
                var selectedStatus = useOperator
                    ? operatorSnapshot.Status
                    : leagueSnapshot.Status;
                var selectedUpdatedAt = useOperator
                    ? operatorSnapshot.UpdatedAtUtc
                    : leagueSnapshot.UpdatedAtUtc;

                var shouldShow = selectedActive;
                if (selectedActive)
                {
                    _keepVisibleUntilUtc = DateTimeOffset.UtcNow.AddSeconds(3);
                }
                else if (selectedStatus is "completed" or "blocked" or "stopped")
                {
                    var target = selectedUpdatedAt.AddSeconds(12);
                    if (_keepVisibleUntilUtc < target)
                        _keepVisibleUntilUtc = target;
                    shouldShow = DateTimeOffset.UtcNow <= _keepVisibleUntilUtc;
                }

                if (shouldShow)
                {
                    var text = useOperator
                        ? BuildOperatorText(operatorSnapshot)
                        : BuildLeagueText(leagueSnapshot);

                    if (!string.Equals(text, _lastText, StringComparison.Ordinal))
                    {
                        SetWindowText(_text, text);
                        _lastText = text;
                    }

                    UpdateButtons(operatorSnapshot, leagueSnapshot, useOperator);
                    PositionConsole(_window);
                    ShowWindow(_window, SwShowNoActivate);
                    _ = SetWindowPos(
                        _window,
                        new IntPtr(-1),
                        0,
                        0,
                        0,
                        0,
                        0x0001 | 0x0002 | 0x0010);
                }
                else
                {
                    ShowWindow(_window, SwHide);
                }

                Thread.Sleep(180);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "AI Operator Console đã dừng ngoài dự kiến.");
        }
        finally
        {
            if (_window != IntPtr.Zero)
            {
                DestroyWindow(_window);
                _window = IntPtr.Zero;
            }

            if (!string.IsNullOrWhiteSpace(_registeredClass))
                _ = UnregisterClass(_registeredClass, GetModuleHandle(null));
        }
    }

    private IntPtr CreateButton(
        string label,
        int x,
        int y,
        int id,
        IntPtr instance) =>
        CreateWindowEx(
            0,
            "BUTTON",
            label,
            WsChild | WsVisible | BsPushButton,
            x,
            y,
            ButtonWidth,
            ButtonHeight,
            _window,
            new IntPtr(id),
            instance,
            IntPtr.Zero);

    private void UpdateButtons(
        ComputerOperatorProgressSnapshot operatorSnapshot,
        LeagueVisualProgressSnapshot leagueSnapshot,
        bool useOperator)
    {
        var operatorRunning = useOperator && operatorSnapshot.Active;
        _ = EnableWindow(
            _pauseButton,
            operatorRunning && !operatorSnapshot.Paused);
        _ = EnableWindow(
            _resumeButton,
            operatorRunning && operatorSnapshot.Paused);
        _ = EnableWindow(
            _stopButton,
            operatorRunning || leagueSnapshot.Active);
    }

    private IntPtr WindowProc(
        IntPtr handle,
        uint message,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (message == WmCommand)
        {
            var id = unchecked((int)(wParam.ToInt64() & 0xFFFF));
            switch (id)
            {
                case IdPause:
                    if (execution.Pause())
                        operatorProgress.Pause();
                    return IntPtr.Zero;

                case IdResume:
                    if (execution.Resume())
                        operatorProgress.Resume();
                    return IntPtr.Zero;

                case IdStop:
                    execution.Stop();
                    control.Stop();
                    operatorProgress.StopByUser(
                        "Người dùng đã nhấn DỪNG NGAY trên AI Operator Console.");
                    logger.LogWarning(
                        "Người dùng đã dừng Computer Operator từ console nổi.");
                    return IntPtr.Zero;
            }
        }

        if (message == WmCtlColorStatic)
        {
            _ = SetTextColor(wParam, 0x00F4F4F4);
            _ = SetBkColor(wParam, 0x00111111);
            return _blackBrush;
        }

        if (message == WmClose)
        {
            ShowWindow(handle, SwHide);
            return IntPtr.Zero;
        }

        if (message == WmDestroy)
            return IntPtr.Zero;

        return DefWindowProc(handle, message, wParam, lParam);
    }

    private static string BuildOperatorText(
        ComputerOperatorProgressSnapshot snapshot)
    {
        var builder = new StringBuilder();

        var heading = snapshot.Status switch
        {
            "paused" => "Ⅱ AI OPERATOR ĐANG TẠM DỪNG",
            "completed" => "✓ AI OPERATOR HOÀN TẤT",
            "blocked" => "⚠ AI OPERATOR BỊ CHẶN",
            "stopped" => "■ AI OPERATOR ĐÃ DỪNG",
            _ => "● AI OPERATOR ĐANG LÀM"
        };

        builder.AppendLine(heading);
        builder.AppendLine(
            $"Giai đoạn: {snapshot.CurrentStage.ToUpperInvariant()}   Quan sát: {snapshot.ObservationCount}   Hành động: {snapshot.ActionCount}");

        if (snapshot.StartedAtUtc is DateTimeOffset started)
        {
            var elapsed = DateTimeOffset.UtcNow - started;
            builder.AppendLine(
                $"Thời gian task: {elapsed.TotalSeconds:0.0}s   Cập nhật cuối: {(DateTimeOffset.UtcNow - snapshot.UpdatedAtUtc).TotalSeconds:0.0}s trước");
        }

        if (snapshot.Stale)
        {
            builder.AppendLine(
                $"⚠ WATCHDOG: Không có bước mới trong {snapshot.StaleSeconds}s. Có thể đang chờ dịch vụ ngoài hoặc bị treo.");
        }

        builder.AppendLine("Ctrl + Shift + F12 hoặc nút DỪNG NGAY để hủy.");
        builder.AppendLine(new string('─', 66));

        foreach (var entry in snapshot.Entries.TakeLast(11))
        {
            var time = entry.AtUtc.ToLocalTime().ToString("HH:mm:ss");
            var confidence = entry.Confidence is double value
                ? $" [{value * 100:0}%]"
                : string.Empty;
            var elapsed = entry.ElapsedSincePreviousMs is long milliseconds
                ? $" +{milliseconds / 1000.0:0.00}s"
                : string.Empty;

            builder.AppendLine(
                $"{time}  {entry.Stage.ToUpperInvariant()}{confidence}{elapsed}");
            builder.AppendLine($"  {Limit(entry.Message, 108)}");
        }

        return builder.ToString();
    }

    private static string BuildLeagueText(
        LeagueVisualProgressSnapshot snapshot)
    {
        var builder = new StringBuilder();
        var heading = snapshot.Active
            ? "● LEAGUE AGENT ĐANG LÀM"
            : snapshot.Status == "completed"
                ? "✓ LEAGUE AGENT HOÀN TẤT"
                : "⚠ LEAGUE AGENT ĐÃ DỪNG";

        builder.AppendLine(heading);
        builder.AppendLine(
            $"Quan sát: {snapshot.ObservationCount}   Click: {snapshot.ClickCount}");
        builder.AppendLine("Ctrl + Shift + F12 hoặc nút DỪNG NGAY để hủy.");
        builder.AppendLine(new string('─', 66));

        foreach (var entry in snapshot.Entries.TakeLast(11))
        {
            var time = entry.AtUtc.ToLocalTime().ToString("HH:mm:ss");
            var confidence = entry.Confidence is double value
                ? $" [{value * 100:0}%]"
                : string.Empty;
            var coordinates =
                entry.X is int x && entry.Y is int y && (x != 0 || y != 0)
                    ? $" ({x},{y})"
                    : string.Empty;

            builder.AppendLine(
                $"{time}  {entry.Stage.ToUpperInvariant()}{confidence}{coordinates}");
            builder.AppendLine($"  {Limit(entry.Message, 108)}");
        }

        return builder.ToString();
    }

    private static void PositionConsole(IntPtr handle)
    {
        var screenWidth = Math.Max(
            ConsoleWidth + Margin * 2,
            GetSystemMetrics(SmCxScreen));
        var screenHeight = Math.Max(
            ConsoleHeight + Margin * 2,
            GetSystemMetrics(SmCyScreen));
        var x = Math.Max(
            Margin,
            screenWidth - ConsoleWidth - Margin);
        var y = Math.Min(
            Math.Max(Margin, 72),
            Math.Max(Margin, screenHeight - ConsoleHeight - Margin));

        _ = SetWindowPos(
            handle,
            new IntPtr(-1),
            x,
            y,
            ConsoleWidth,
            ConsoleHeight,
            0x0010);
    }

    private static string Limit(
        string value,
        int maximum)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty).Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= maximum
            ? normalized
            : normalized[..Math.Max(1, maximum - 1)] + "…";
    }

    private static void PumpMessages()
    {
        while (PeekMessage(
            out var message,
            IntPtr.Zero,
            0,
            0,
            PmRemove))
        {
            _ = TranslateMessage(ref message);
            _ = DispatchMessage(ref message);
        }
    }

    public void Dispose()
    {
        _shutdown?.Cancel();
        _shutdown?.Dispose();
    }

    private delegate IntPtr WindowProcedure(
        IntPtr handle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string? ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr HWnd;
        public uint Value;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(
        ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(
        string className,
        IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(
        IntPtr handle,
        string text);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(
        IntPtr handle,
        int command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(
        IntPtr handle,
        uint colorKey,
        byte alpha,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(
        IntPtr handle,
        uint affinity);

    [DllImport("user32.dll")]
    private static extern bool EnableWindow(
        IntPtr handle,
        bool enable);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(
        int index);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(
        out Message message,
        IntPtr handle,
        uint filterMin,
        uint filterMax,
        uint removeMessage);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(
        ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(
        ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(
        IntPtr handle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr handle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(
        int objectIndex);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(
        IntPtr hdc,
        uint color);

    [DllImport("gdi32.dll")]
    private static extern uint SetBkColor(
        IntPtr hdc,
        uint color);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(
        string? moduleName);
}
