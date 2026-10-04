using System.Runtime.InteropServices;
using System.Text;

namespace PersonalAI.Web.Services;

public sealed class WindowsAiOperatorConsoleService
    : IHostedService, IDisposable
{
    private readonly LeagueVisualProgressStore leagueProgress;
    private readonly ComputerOperatorProgressStore operatorProgress;
    private readonly ComputerOperatorExecutionControl execution;
    private readonly ComputerControlGate control;
    private readonly ILogger<WindowsAiOperatorConsoleService> logger;
    private const int ConsoleWidth = 680;
    private const int ConsoleHeight = 560;
    private const int Margin = 18;
    private const int ButtonHeight = 30;
    private const int ButtonWidth = 116;
    private const int Gap = 8;

    private const uint WsPopup = 0x80000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsChild = 0x40000000;
    private const uint WsVScroll = 0x00200000;
    private const uint EsMultiline = 0x0004;
    private const uint EsAutoVScroll = 0x0040;
    private const uint EsReadOnly = 0x0800;
    private const uint EsNoHideSel = 0x0100;
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
    private const uint WmCtlColorEdit = 0x0133;
    private const uint EmSetSel = 0x00B1;
    private const uint EmScrollCaret = 0x00B7;
    private const uint EmLineScroll = 0x00B6;
    private const uint EmGetFirstVisibleLine = 0x00CE;
    private const int EnVScroll = 0x0602;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const int DefaultGuiFont = 17;
    private const int BlackBrush = 4;
    private const int IdPause = 4101;
    private const int IdResume = 4102;
    private const int IdStop = 4103;
    private const int IdClose = 4104;
    private const int IdTail = 4105;
    private const int IdLog = 4106;

    private readonly object _sync = new();
    private readonly WindowProcedure _windowProcedure;
    private Thread? _thread;
    private CancellationTokenSource? _shutdown;
    private IntPtr _window;
    private IntPtr _text;
    private IntPtr _pauseButton;
    private IntPtr _resumeButton;
    private IntPtr _stopButton;
    private IntPtr _closeButton;
    private IntPtr _tailButton;
    private IntPtr _blackBrush;
    private string _lastText = string.Empty;
    private string? _registeredClass;
    private volatile bool _started;
    private volatile bool _windowCreated;
    private volatile bool _visible;
    private string? _lastError;
    private long _forceVisibleUntilUtcTicks;
    private long _lastLoopUtcTicks;
    private long _lastTaskStartedAtUtcTicks;
    private volatile bool _userHidden;
    private volatile bool _followTail = true;
    private volatile bool _programmaticLogScroll;

    public object GetDiagnosticStatus()
    {
        var loopTicks = Interlocked.Read(ref _lastLoopUtcTicks);
        var forceTicks = Interlocked.Read(ref _forceVisibleUntilUtcTicks);
        var nowTicks = DateTimeOffset.UtcNow.UtcTicks;

        return new
        {
            started = _started,
            windowCreated = _windowCreated,
            visible = _visible,
            nativeVisible = _window != IntPtr.Zero && IsWindowVisible(_window),
            userHidden = _userHidden,
            lastError = _lastError,
            environmentUserInteractive = Environment.UserInteractive,
            operatingSystem = Environment.OSVersion.ToString(),
            loopAlive = loopTicks > 0 && nowTicks - loopTicks < TimeSpan.FromSeconds(2).Ticks,
            lastLoopAtUtc = loopTicks > 0
                ? new DateTimeOffset(loopTicks, TimeSpan.Zero)
                : (DateTimeOffset?)null,
            forceVisibleUntilUtc = forceTicks > 0
                ? new DateTimeOffset(forceTicks, TimeSpan.Zero)
                : (DateTimeOffset?)null
        };
    }

    public void ShowTestConsole(TimeSpan duration)
    {
        _userHidden = false;
        var until = DateTimeOffset.UtcNow.Add(
            duration <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : duration);
        Interlocked.Exchange(
            ref _forceVisibleUntilUtcTicks,
            until.UtcTicks);
    }

    public bool TryGetVisibleBounds(
        out int left,
        out int top,
        out int width,
        out int height)
    {
        left = 0;
        top = 0;
        width = 0;
        height = 0;

        var handle = _window;
        if (handle == IntPtr.Zero || !IsWindowVisible(handle))
            return false;

        if (!GetWindowRect(handle, out var rect))
            return false;

        var rectWidth = rect.Right - rect.Left;
        var rectHeight = rect.Bottom - rect.Top;
        if (rectWidth <= 0 || rectHeight <= 0)
            return false;

        left = rect.Left;
        top = rect.Top;
        width = rectWidth;
        height = rectHeight;
        return true;
    }

    public WindowsAiOperatorConsoleService(
        LeagueVisualProgressStore leagueProgress,
        ComputerOperatorProgressStore operatorProgress,
        ComputerOperatorExecutionControl execution,
        ComputerControlGate control,
        ILogger<WindowsAiOperatorConsoleService> logger)
    {
        this.leagueProgress = leagueProgress;
        this.operatorProgress = operatorProgress;
        this.execution = execution;
        this.control = control;
        this.logger = logger;
        _windowProcedure = WindowProc;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return Task.CompletedTask;

        logger.LogInformation(
            "AI Operator Console đang khởi động. UserInteractive={UserInteractive}.",
            Environment.UserInteractive);

        lock (_sync)
        {
            if (_thread is not null)
                return Task.CompletedTask;

            _shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _started = true;
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
                var error = Marshal.GetLastWin32Error();
                _lastError = $"RegisterClassEx failed: {error}";
                logger.LogWarning(
                    "Không đăng ký được lớp cửa sổ AI Operator Console. Win32={Error}.",
                    error);
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
                var error = Marshal.GetLastWin32Error();
                _lastError = $"CreateWindowEx failed: {error}";
                logger.LogWarning(
                    "Không tạo được AI Operator Console. Win32={Error}.",
                    error);
                return;
            }

            _windowCreated = true;
            _lastError = null;
            logger.LogInformation(
                "AI Operator Console đã tạo cửa sổ native thành công. Handle=0x{Handle}.",
                _window.ToInt64().ToString("X"));

            _ = SetLayeredWindowAttributes(_window, 0, 244, LwaAlpha);
            // Console phải xuất hiện trong screenshot người dùng chụp để có thể
            // gửi log chẩn đoán. Ảnh nội bộ gửi Desktop Vision sẽ che riêng
            // vùng console trong WindowsDesktopScreenshotService.
            _ = SetWindowDisplayAffinity(_window, 0x00000000); // WDA_NONE

            var font = GetStockObject(DefaultGuiFont);
            _text = CreateWindowEx(
                0,
                "EDIT",
                "AI Operator Console đang chờ tác vụ…",
                WsChild | WsVisible | WsVScroll | EsMultiline | EsAutoVScroll | EsReadOnly | EsNoHideSel,
                14,
                54,
                ConsoleWidth - 28,
                ConsoleHeight - 68,
                _window,
                new IntPtr(IdLog),
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
            _tailButton = CreateButton(
                "VỀ CUỐI ↓",
                14 + (ButtonWidth + Gap) * 3,
                14,
                IdTail,
                instance);
            _closeButton = CreateButton(
                "ĐÓNG",
                14 + (ButtonWidth + Gap) * 4,
                14,
                IdClose,
                instance);

            foreach (var handle in new[] { _text, _pauseButton, _resumeButton, _stopButton, _tailButton, _closeButton })
            {
                if (handle != IntPtr.Zero && font != IntPtr.Zero)
                    _ = SendMessage(handle, WmSetFont, font, new IntPtr(1));
            }

            ShowWindow(_window, SwHide);

            while (!cancellationToken.IsCancellationRequested)
            {
                Interlocked.Exchange(
                    ref _lastLoopUtcTicks,
                    DateTimeOffset.UtcNow.UtcTicks);

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
                var selectedStartedAt = useOperator
                    ? operatorSnapshot.StartedAtUtc
                    : leagueSnapshot.StartedAtUtc;

                var startedTicks = selectedStartedAt?.UtcTicks ?? 0;
                if (startedTicks > 0 &&
                    startedTicks != Interlocked.Read(ref _lastTaskStartedAtUtcTicks))
                {
                    Interlocked.Exchange(
                        ref _lastTaskStartedAtUtcTicks,
                        startedTicks);
                    _userHidden = false;
                }

                var forceVisibleTicks = Interlocked.Read(
                    ref _forceVisibleUntilUtcTicks);
                var testVisible =
                    forceVisibleTicks > 0 &&
                    DateTimeOffset.UtcNow.UtcTicks <= forceVisibleTicks;

                var hasDisplayableState =
                    selectedActive ||
                    selectedStatus is "completed" or "blocked" or "stopped" or "paused";

                // Console hoạt động như Output window: giữ nguyên trạng thái cuối
                // cho đến khi người dùng chủ động bấm ĐÓNG. Không còn tự ẩn.
                var shouldShow =
                    testVisible ||
                    (!_userHidden && hasDisplayableState);

                if (shouldShow)
                {
                    var text = testVisible && !selectedActive
                        ? BuildTestText()
                        : useOperator
                            ? BuildOperatorText(operatorSnapshot)
                            : BuildLeagueText(leagueSnapshot);

                    if (!string.Equals(text, _lastText, StringComparison.Ordinal))
                    {
                        var firstVisibleLine = !_followTail && _text != IntPtr.Zero
                            ? SendMessage(
                                _text,
                                EmGetFirstVisibleLine,
                                IntPtr.Zero,
                                IntPtr.Zero).ToInt32()
                            : 0;

                        SetWindowText(_text, text);
                        _lastText = text;

                        if (_followTail)
                        {
                            ScrollLogToEnd();
                        }
                        else if (firstVisibleLine > 0)
                        {
                            _programmaticLogScroll = true;
                            try
                            {
                                _ = SendMessage(
                                    _text,
                                    EmLineScroll,
                                    IntPtr.Zero,
                                    new IntPtr(firstVisibleLine));
                            }
                            finally
                            {
                                _programmaticLogScroll = false;
                            }
                        }
                    }

                    UpdateButtons(operatorSnapshot, leagueSnapshot, useOperator);
                    PositionConsole(_window);
                    ShowWindow(_window, SwShowNoActivate);
                    _visible = true;
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
                    _visible = false;
                }

                Thread.Sleep(180);
            }
        }
        catch (Exception exception)
        {
            _lastError = exception.ToString();
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
            operatorRunning && execution.Pausable && !operatorSnapshot.Paused);
        _ = EnableWindow(
            _resumeButton,
            operatorRunning && execution.Pausable && operatorSnapshot.Paused);
        _ = EnableWindow(
            _stopButton,
            operatorRunning || leagueSnapshot.Active);
        _ = EnableWindow(
            _tailButton,
            true);
        _ = EnableWindow(
            _closeButton,
            true);
    }

    private IntPtr WindowProc(
        IntPtr handle,
        uint message,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (message == WmCommand)
        {
            var rawCommand = wParam.ToInt64();
            var id = unchecked((int)(rawCommand & 0xFFFF));
            var notification = unchecked((int)((rawCommand >> 16) & 0xFFFF));

            if (id == IdLog &&
                notification == EnVScroll &&
                !_programmaticLogScroll)
            {
                _followTail = false;
                return IntPtr.Zero;
            }

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

                case IdTail:
                    _followTail = true;
                    ScrollLogToEnd();
                    return IntPtr.Zero;

                case IdClose:
                    _userHidden = true;
                    ShowWindow(_window, SwHide);
                    _visible = false;
                    logger.LogInformation(
                        "Người dùng đã đóng AI Operator Console. Console sẽ tự hiện lại khi có task mới.");
                    return IntPtr.Zero;
            }
        }

        if (message == WmCtlColorStatic || message == WmCtlColorEdit)
        {
            _ = SetTextColor(wParam, 0x00F4F4F4);
            _ = SetBkColor(wParam, 0x00111111);
            return _blackBrush;
        }

        if (message == WmClose)
        {
            _userHidden = true;
            ShowWindow(handle, SwHide);
            _visible = false;
            return IntPtr.Zero;
        }

        if (message == WmDestroy)
            return IntPtr.Zero;

        return DefWindowProc(handle, message, wParam, lParam);
    }

    private static string BuildTestText() =>
        """
● AI OPERATOR CONSOLE · TEST

Console native đang hoạt động.
Nếu bạn nhìn thấy bảng này thì lớp hiển thị Win32 đã hoạt động bình thường.

[TẠM DỪNG] [TIẾP TỤC] [DỪNG NGAY] [ĐÓNG]

Console chỉ ẩn khi bạn bấm ĐÓNG; task mới sẽ tự hiện lại.
Ảnh nội bộ gửi Desktop Vision sẽ che vùng console.
""";

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
            var end = snapshot.Active
                ? DateTimeOffset.UtcNow
                : snapshot.UpdatedAtUtc;
            var elapsed = end - started;
            var lastUpdateAgo = DateTimeOffset.UtcNow - snapshot.UpdatedAtUtc;

            builder.AppendLine(
                $"Thời gian task: {Math.Max(0, elapsed.TotalSeconds):0.0}s   Cập nhật cuối: {Math.Max(0, lastUpdateAgo.TotalSeconds):0.0}s trước");
        }

        if (snapshot.Stale)
        {
            builder.AppendLine(
                $"⚠ WATCHDOG: Không có bước mới trong {snapshot.StaleSeconds}s. Có thể đang chờ dịch vụ ngoài hoặc bị treo.");
        }

        builder.AppendLine("Ctrl + Shift + F12 hoặc DỪNG NGAY để hủy; ĐÓNG chỉ ẩn console.");
        builder.AppendLine(new string('─', 66));

        foreach (var entry in snapshot.Entries.TakeLast(160))
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
        builder.AppendLine("Ctrl + Shift + F12 hoặc DỪNG NGAY để hủy; ĐÓNG chỉ ẩn console.");
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

    private void ScrollLogToEnd()
    {
        if (_text == IntPtr.Zero)
            return;

        _programmaticLogScroll = true;
        try
        {
            _ = SendMessage(
                _text,
                EmSetSel,
                new IntPtr(-1),
                new IntPtr(-1));
            _ = SendMessage(
                _text,
                EmScrollCaret,
                IntPtr.Zero,
                IntPtr.Zero);
        }
        finally
        {
            _programmaticLogScroll = false;
        }
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
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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
    private static extern bool IsWindowVisible(
        IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(
        IntPtr handle,
        out Rect rect);

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
