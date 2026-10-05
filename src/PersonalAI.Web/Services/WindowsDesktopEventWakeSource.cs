using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PersonalAI.Web.Services;

public static class DesktopSystemEventKinds
{
    public const string ForegroundChanged = "foreground-changed";
    public const string WindowCreated = "window-created";
    public const string WindowDestroyed = "window-destroyed";
    public const string WindowShown = "window-shown";
    public const string WindowHidden = "window-hidden";
    public const string WindowMovedOrResized = "window-moved-or-resized";
    public const string WindowNameChanged = "window-name-changed";
}

public sealed record DesktopSystemEvent(
    string Kind,
    string WindowId,
    DateTimeOffset OccurredAtUtc,
    uint NativeEvent,
    string Reason);

public sealed record DesktopObservationWakeResult(
    bool EventReceived,
    DesktopSystemEvent? Event,
    TimeSpan Waited,
    string Reason);

public interface IAdaptiveObservationWakeSource
{
    bool EventDrivenAvailable { get; }

    Task<DesktopObservationWakeResult> WaitAsync(
        TimeSpan fallbackDelay,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsDesktopEventWakeSource
    : IAdaptiveObservationWakeSource, IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectHide = 0x8003;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint EventObjectNameChange = 0x800C;
    private const uint WineventOutofcontext = 0x0000;
    private const uint WineventSkipownprocess = 0x0002;
    private const int ObjidWindow = 0;
    private const uint WmQuit = 0x0012;

    private readonly ConcurrentQueue<DesktopSystemEvent> events = new();
    private readonly SemaphoreSlim signal = new(0, int.MaxValue);
    private readonly ManualResetEventSlim ready = new(false);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Thread? hookThread;
    private WinEventDelegate? callback;
    private nint hook;
    private uint hookThreadId;
    private bool disposed;

    public WindowsDesktopEventWakeSource()
    {
        if (!OperatingSystem.IsWindows())
            return;

        hookThread = new Thread(HookLoop)
        {
            IsBackground = true,
            Name = "AI-Ca-Nhan-WinEventHook"
        };
        hookThread.Start();
        _ = ready.Wait(TimeSpan.FromSeconds(2));
    }

    public bool EventDrivenAvailable =>
        OperatingSystem.IsWindows() &&
        hook != nint.Zero &&
        hookThread?.IsAlive == true;

    public async Task<DesktopObservationWakeResult> WaitAsync(
        TimeSpan fallbackDelay,
        CancellationToken cancellationToken = default)
    {
        if (fallbackDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(fallbackDelay),
                "Khoảng chờ fallback phải lớn hơn 0.");

        var started = DateTimeOffset.UtcNow;

        if (!EventDrivenAvailable)
        {
            await Task.Delay(
                fallbackDelay,
                cancellationToken);

            return new(
                EventReceived: false,
                Event: null,
                DateTimeOffset.UtcNow - started,
                "WinEventHook chưa khả dụng; đã dùng polling fallback.");
        }

        if (TryDequeueLatest(out var queued))
        {
            return new(
                EventReceived: true,
                queued,
                DateTimeOffset.UtcNow - started,
                $"Được đánh thức ngay bởi sự kiện Windows: {queued!.Reason}");
        }

        var timeoutTask =
            Task.Delay(
                fallbackDelay,
                cancellationToken);

        var eventTask =
            signal.WaitAsync(
                cancellationToken);

        var completed =
            await Task.WhenAny(
                eventTask,
                timeoutTask);

        cancellationToken.ThrowIfCancellationRequested();

        if (completed == eventTask &&
            await eventTask &&
            TryDequeueLatest(out var received))
        {
            return new(
                EventReceived: true,
                received,
                DateTimeOffset.UtcNow - started,
                $"WinEventHook đánh thức verifier: {received!.Reason}");
        }

        return new(
            EventReceived: false,
            Event: null,
            DateTimeOffset.UtcNow - started,
            "Không có Windows event trước poll deadline; tiếp tục polling fallback.");
    }

    private bool TryDequeueLatest(
        out DesktopSystemEvent? result)
    {
        result = null;

        while (events.TryDequeue(
                   out var item))
        {
            result = item;
        }

        return result is not null;
    }

    private void HookLoop()
    {
        hookThreadId =
            GetCurrentThreadId();

        callback = OnWinEvent;

        hook = SetWinEventHook(
            EventSystemForeground,
            EventObjectNameChange,
            nint.Zero,
            callback,
            0,
            0,
            WineventOutofcontext |
            WineventSkipownprocess);

        ready.Set();

        if (hook == nint.Zero)
            return;

        while (!lifetime.IsCancellationRequested &&
               GetMessage(
                   out var message,
                   nint.Zero,
                   0,
                   0) > 0)
        {
            TranslateMessage(
                ref message);
            DispatchMessage(
                ref message);
        }

        _ = UnhookWinEvent(hook);
        hook = nint.Zero;
    }

    private void OnWinEvent(
        nint winEventHook,
        uint nativeEvent,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (window == nint.Zero)
            return;

        var relevantObject =
            nativeEvent == EventSystemForeground ||
            (objectId == ObjidWindow &&
             childId == 0);

        if (!relevantObject)
            return;

        var kind =
            nativeEvent switch
            {
                EventSystemForeground =>
                    DesktopSystemEventKinds.ForegroundChanged,
                EventObjectCreate =>
                    DesktopSystemEventKinds.WindowCreated,
                EventObjectDestroy =>
                    DesktopSystemEventKinds.WindowDestroyed,
                EventObjectShow =>
                    DesktopSystemEventKinds.WindowShown,
                EventObjectHide =>
                    DesktopSystemEventKinds.WindowHidden,
                EventObjectLocationChange =>
                    DesktopSystemEventKinds.WindowMovedOrResized,
                EventObjectNameChange =>
                    DesktopSystemEventKinds.WindowNameChanged,
                _ => string.Empty
            };

        if (string.IsNullOrWhiteSpace(kind))
            return;

        var reason =
            kind switch
            {
                DesktopSystemEventKinds.ForegroundChanged =>
                    "Foreground window thay đổi.",
                DesktopSystemEventKinds.WindowCreated =>
                    "Windows tạo cửa sổ mới.",
                DesktopSystemEventKinds.WindowDestroyed =>
                    "Một cửa sổ đã đóng.",
                DesktopSystemEventKinds.WindowShown =>
                    "Một cửa sổ vừa hiển thị.",
                DesktopSystemEventKinds.WindowHidden =>
                    "Một cửa sổ vừa ẩn.",
                DesktopSystemEventKinds.WindowMovedOrResized =>
                    "Cửa sổ vừa di chuyển hoặc đổi kích thước.",
                DesktopSystemEventKinds.WindowNameChanged =>
                    "Tên/tiêu đề cửa sổ vừa thay đổi.",
                _ =>
                    "Windows UI state vừa thay đổi."
            };

        events.Enqueue(
            new(
                kind,
                $"0x{window.ToInt64():X}",
                DateTimeOffset.UtcNow,
                nativeEvent,
                reason));

        try
        {
            signal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        lifetime.Cancel();

        if (hookThreadId != 0)
        {
            _ = PostThreadMessage(
                hookThreadId,
                WmQuit,
                nint.Zero,
                nint.Zero);
        }

        if (hookThread?.IsAlive == true)
            _ = hookThread.Join(
                TimeSpan.FromSeconds(1));

        signal.Dispose();
        ready.Dispose();
        lifetime.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint HWnd;
        public uint MessageId;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public Point Pt;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private delegate void WinEventDelegate(
        nint winEventHook,
        uint nativeEvent,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint module,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(
        nint winEventHook);

    [DllImport("user32.dll")]
    private static extern int GetMessage(
        out Message message,
        nint window,
        uint messageFilterMin,
        uint messageFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(
        ref Message message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(
        ref Message message);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
