using System.Runtime.InteropServices;
using System.Text;

namespace PersonalAI.Web.Services;

public sealed class WindowsLeagueVisualOverlayService(
    LeagueVisualProgressStore progress,
    ComputerOperatorProgressStore computerProgress,
    ILogger<WindowsLeagueVisualOverlayService> logger)
    : IHostedService, IDisposable
{
    private const int OverlayWidth = 470;
    private const int OverlayHeight = 330;
    private const int Margin = 18;

    private const uint WsPopup = 0x80000000;
    private const uint WsVisible = 0x10000000;
    private const uint SsLeft = 0x00000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExTransparent = 0x00000020;
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
    private const int DefaultGuiFont = 17;

    private readonly object _sync = new();
    private Thread? _thread;
    private CancellationTokenSource? _stop;
    private IntPtr _window;
    private DateTimeOffset _keepVisibleUntilUtc;
    private string _lastText = string.Empty;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            return Task.CompletedTask;

        lock (_sync)
        {
            if (_thread is not null)
                return Task.CompletedTask;

            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _thread = new Thread(() => Run(_stop.Token))
            {
                IsBackground = true,
                Name = "PersonalAI-League-Progress-Overlay"
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
            _stop?.Cancel();
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
            _window = CreateOverlay();
            if (_window == IntPtr.Zero)
            {
                logger.LogWarning(
                    "Không tạo được cửa sổ overlay tiến trình League. Win32={Error}.",
                    Marshal.GetLastWin32Error());
                return;
            }

            ShowWindow(_window, SwHide);

            while (!cancellationToken.IsCancellationRequested)
            {
                PumpMessages();

                var leagueSnapshot = progress.Get();
                var operatorSnapshot = computerProgress.Get();

                var useOperator =
                    operatorSnapshot.Active ||
                    (!leagueSnapshot.Active &&
                     operatorSnapshot.Status is "completed" or "blocked" &&
                     operatorSnapshot.UpdatedAtUtc >= leagueSnapshot.UpdatedAtUtc);

                var shouldShow = useOperator
                    ? operatorSnapshot.Active
                    : leagueSnapshot.Active;

                var selectedUpdatedAt = useOperator
                    ? operatorSnapshot.UpdatedAtUtc
                    : leagueSnapshot.UpdatedAtUtc;

                var selectedStatus = useOperator
                    ? operatorSnapshot.Status
                    : leagueSnapshot.Status;

                if (shouldShow)
                {
                    _keepVisibleUntilUtc = DateTimeOffset.UtcNow.AddSeconds(3);
                }
                else if (selectedStatus is "completed" or "blocked")
                {
                    if (_keepVisibleUntilUtc < selectedUpdatedAt.AddSeconds(10))
                        _keepVisibleUntilUtc = selectedUpdatedAt.AddSeconds(10);
                    shouldShow = DateTimeOffset.UtcNow <= _keepVisibleUntilUtc;
                }

                if (shouldShow)
                {
                    var text = useOperator
                        ? BuildText(operatorSnapshot)
                        : BuildText(leagueSnapshot);
                    if (!string.Equals(text, _lastText, StringComparison.Ordinal))
                    {
                        SetWindowText(_window, text);
                        _lastText = text;
                    }

                    PositionOverlay(_window);
                    ShowWindow(_window, SwShowNoActivate);
                    SetWindowPos(
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

                Thread.Sleep(220);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Overlay tiến trình League đã dừng ngoài dự kiến.");
        }
        finally
        {
            if (_window != IntPtr.Zero)
            {
                DestroyWindow(_window);
                _window = IntPtr.Zero;
            }
        }
    }

    private static IntPtr CreateOverlay()
    {
        var exStyle =
            WsExTopmost |
            WsExToolWindow |
            WsExNoActivate |
            WsExTransparent |
            WsExLayered;

        var handle = CreateWindowEx(
            exStyle,
            "STATIC",
            "AI đang chuẩn bị automation…",
            WsPopup | WsVisible | SsLeft,
            0,
            0,
            OverlayWidth,
            OverlayHeight,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (handle == IntPtr.Zero)
            return IntPtr.Zero;

        _ = SetLayeredWindowAttributes(handle, 0, 238, LwaAlpha);
        _ = SetWindowDisplayAffinity(handle, 0x00000011); // WDA_EXCLUDEFROMCAPTURE
        var font = GetStockObject(DefaultGuiFont);
        if (font != IntPtr.Zero)
            _ = SendMessage(handle, WmSetFont, font, new IntPtr(1));

        return handle;
    }

    private static void PositionOverlay(IntPtr handle)
    {
        var screenWidth = Math.Max(OverlayWidth + Margin * 2, GetSystemMetrics(SmCxScreen));
        var screenHeight = Math.Max(OverlayHeight + Margin * 2, GetSystemMetrics(SmCyScreen));
        var x = Math.Max(Margin, screenWidth - OverlayWidth - Margin);
        var y = Math.Min(
            Math.Max(Margin, 72),
            Math.Max(Margin, screenHeight - OverlayHeight - Margin));

        _ = SetWindowPos(
            handle,
            new IntPtr(-1),
            x,
            y,
            OverlayWidth,
            OverlayHeight,
            0x0010);
    }

    private static string BuildText(
        ComputerOperatorProgressSnapshot snapshot)
    {
        var builder = new StringBuilder();
        var heading = snapshot.Active
            ? "● COMPUTER OPERATOR ĐANG LÀM"
            : snapshot.Status == "completed"
                ? "✓ COMPUTER OPERATOR HOÀN TẤT"
                : "⚠ COMPUTER OPERATOR ĐÃ DỪNG";

        builder.AppendLine(heading);
        builder.AppendLine(
            $"Quan sát: {snapshot.ObservationCount}   Hành động: {snapshot.ActionCount}");
        builder.AppendLine("Ctrl + Shift + F12: dừng khẩn cấp");
        builder.AppendLine(new string('─', 46));

        foreach (var entry in snapshot.Entries.TakeLast(8))
        {
            var time = entry.AtUtc.ToLocalTime().ToString("HH:mm:ss");
            var confidence = entry.Confidence is double value
                ? $" [{value * 100:0}%]"
                : string.Empty;

            builder.AppendLine(
                $"{time}  {entry.Stage.ToUpperInvariant()}{confidence}");
            builder.AppendLine($"  {Limit(entry.Message, 92)}");
        }

        return builder.ToString();
    }

    private static string BuildText(LeagueVisualProgressSnapshot snapshot)
    {
        var builder = new StringBuilder();
        var heading = snapshot.Active
            ? "● AI ĐANG THAO TÁC"
            : snapshot.Status == "completed"
                ? "✓ AUTOMATION HOÀN TẤT"
                : "⚠ AUTOMATION ĐÃ DỪNG";

        builder.AppendLine(heading);
        builder.AppendLine(
            $"Quan sát: {snapshot.ObservationCount}   Click: {snapshot.ClickCount}");
        builder.AppendLine("Ctrl + Shift + F12: dừng khẩn cấp");
        builder.AppendLine(new string('─', 46));

        foreach (var entry in snapshot.Entries.TakeLast(8))
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
            builder.AppendLine($"  {Limit(entry.Message, 92)}");
        }

        return builder.ToString();
    }

    private static string Limit(string value, int maximum)
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
        while (PeekMessage(out var message, IntPtr.Zero, 0, 0, PmRemove))
        {
            _ = TranslateMessage(ref message);
            _ = DispatchMessage(ref message);
        }
    }

    public void Dispose()
    {
        _stop?.Cancel();
        _stop?.Dispose();
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
    private static extern bool SetWindowText(IntPtr handle, string text);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

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
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(
        out Message message,
        IntPtr handle,
        uint filterMin,
        uint filterMax,
        uint removeMessage);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr handle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int objectIndex);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
