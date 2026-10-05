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
    public const string FocusChanged = "focus-changed";
    public const string SelectionChanged = "selection-changed";
    public const string ValueChanged = "value-changed";
    public const string StructureChanged = "structure-changed";
    public const string PropertyChanged = "property-changed";
}

public sealed record DesktopSystemEvent(
    string Kind,
    string WindowId,
    DateTimeOffset OccurredAtUtc,
    uint NativeEvent,
    string Reason,
    string Source = "win-event",
    double SourceConfidence = 0.80,
    bool Corroborated = false,
    string BurstId = "",
    int BurstSize = 1);

public sealed record DesktopObservationWakeResult(
    bool EventReceived,
    DesktopSystemEvent? Event,
    TimeSpan Waited,
    string Reason);

public sealed record DesktopEventObservationSnapshot(
    bool EventDrivenAvailable,
    long ReceivedEvents,
    long CoalescedEvents,
    long DroppedEvents,
    long DeliveredWakeups,
    long PollFallbacks,
    int QueuedEvents,
    DesktopSystemEvent? LastEvent);

public static class DesktopEventBurstPolicy
{
    public static bool BelongsToSameBurst(
        DesktopSystemEvent previous,
        DesktopSystemEvent current,
        TimeSpan maximumGap)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        if (maximumGap <= TimeSpan.Zero)
            return false;

        return previous.WindowId.Equals(
                   current.WindowId,
                   StringComparison.OrdinalIgnoreCase) &&
               current.OccurredAtUtc >= previous.OccurredAtUtc &&
               current.OccurredAtUtc - previous.OccurredAtUtc <= maximumGap;
    }
}

public interface IAdaptiveObservationWakeSource
{
    bool EventDrivenAvailable { get; }

    DesktopEventObservationSnapshot GetSnapshot();

    Task<DesktopObservationWakeResult> WaitAsync(
        TimeSpan fallbackDelay,
        CancellationToken cancellationToken = default);

    Task<DesktopObservationWakeResult> WaitForWindowAsync(
        string? windowId,
        TimeSpan fallbackDelay,
        CancellationToken cancellationToken = default) =>
        WaitAsync(
            fallbackDelay,
            cancellationToken);
}

public sealed class WindowsDesktopEventWakeSource
    : IAdaptiveObservationWakeSource, IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectHide = 0x8003;
    private const uint EventObjectFocus = 0x8005;
    private const uint EventObjectSelection = 0x8006;
    private const uint EventObjectSelectionAdd = 0x8007;
    private const uint EventObjectSelectionRemove = 0x8008;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint EventObjectNameChange = 0x800C;
    private const uint EventObjectValueChange = 0x800E;
    private const uint WineventOutofcontext = 0x0000;
    private const uint WineventSkipownprocess = 0x0002;
    private const int ObjidWindow = 0;
    private const uint WmQuit = 0x0012;
    private const int MaximumQueuedEvents = 256;
    private const long DuplicateWindowMilliseconds = 60;
    private const long CrossSourceCorrelationMilliseconds = 250;
    private const long BurstGroupingMilliseconds = 120;

    private readonly ConcurrentQueue<DesktopSystemEvent> events = new();
    private readonly SemaphoreSlim signal = new(0, int.MaxValue);
    private readonly ManualResetEventSlim ready = new(false);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Thread? hookThread;
    private WinEventDelegate? callback;
    private nint hook;
    private uint hookThreadId;
    private bool disposed;
    private long receivedEvents;
    private long coalescedEvents;
    private long droppedEvents;
    private long deliveredWakeups;
    private long pollFallbacks;
    private long queuedEvents;
    private long lastEventUnixMilliseconds;
    private string lastEventIdentity = string.Empty;
    private DesktopSystemEvent? lastEvent;
    private readonly ConcurrentDictionary<string, DesktopSystemEvent> recentEvidence =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object burstSync = new();
    private readonly Dictionary<string, DesktopSystemEvent> recentBursts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IFlaUiAutomationClient? flaUi;

    public WindowsDesktopEventWakeSource(
        IFlaUiAutomationClient? flaUi = null)
    {
        this.flaUi = flaUi;
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

    public DesktopEventObservationSnapshot GetSnapshot() =>
        new(
            EventDrivenAvailable,
            Interlocked.Read(ref receivedEvents),
            Interlocked.Read(ref coalescedEvents),
            Interlocked.Read(ref droppedEvents),
            Interlocked.Read(ref deliveredWakeups),
            Interlocked.Read(ref pollFallbacks),
            checked((int)Math.Min(
                int.MaxValue,
                Math.Max(
                    0,
                    Interlocked.Read(ref queuedEvents)))),
            Volatile.Read(ref lastEvent));


    public Task<DesktopObservationWakeResult> WaitAsync(
        TimeSpan fallbackDelay,
        CancellationToken cancellationToken = default) =>
        WaitCoreAsync(
            targetWindowId: null,
            fallbackDelay,
            cancellationToken);

    public Task<DesktopObservationWakeResult> WaitForWindowAsync(
        string? windowId,
        TimeSpan fallbackDelay,
        CancellationToken cancellationToken = default) =>
        WaitCoreAsync(
            windowId,
            fallbackDelay,
            cancellationToken);

    private async Task<DesktopObservationWakeResult> WaitCoreAsync(
        string? targetWindowId,
        TimeSpan fallbackDelay,
        CancellationToken cancellationToken = default)
    {
        if (fallbackDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(fallbackDelay),
                "Khoảng chờ fallback phải lớn hơn 0.");

        var started = DateTimeOffset.UtcNow;
        var target =
            (targetWindowId ?? string.Empty)
                .Trim();

        var winEventAvailable =
            EventDrivenAvailable;

        var uiaAvailable =
            target.Length > 0 &&
            flaUi?.Available == true;

        if (!winEventAvailable &&
            !uiaAvailable)
        {
            Interlocked.Increment(
                ref pollFallbacks);

            await Task.Delay(
                fallbackDelay,
                cancellationToken);

            return new(
                EventReceived: false,
                Event: null,
                DateTimeOffset.UtcNow - started,
                "Không có WinEvent/UIA event source khả dụng; đã dùng polling fallback.");
        }

        if (winEventAvailable &&
            TryDequeueLatest(
                targetWindowId,
                out var queued))
        {
            Interlocked.Increment(
                ref deliveredWakeups);

            return new(
                EventReceived: true,
                queued,
                DateTimeOffset.UtcNow - started,
                $"Được đánh thức ngay bởi sự kiện Windows: {queued!.Reason}");
        }

        using var waitCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        var timeoutTask =
            Task.Delay(
                fallbackDelay,
                waitCancellation.Token);

        Task winEventTask =
            winEventAvailable
                ? signal.WaitAsync(
                    waitCancellation.Token)
                : Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    waitCancellation.Token);

        Task<DesktopSystemEvent?> uiaTask =
            uiaAvailable
                ? WaitForUiaEventAsync(
                    target,
                    fallbackDelay,
                    waitCancellation.Token)
                : Task.FromResult<DesktopSystemEvent?>(
                    null);

        var completed =
            await Task.WhenAny(
                winEventTask,
                uiaTask,
                timeoutTask);

        cancellationToken.ThrowIfCancellationRequested();

        if (completed == winEventTask &&
            winEventAvailable)
        {
            await winEventTask;

            if (TryDequeueLatest(
                    targetWindowId,
                    out var received))
            {
                waitCancellation.Cancel();

                Interlocked.Increment(
                    ref deliveredWakeups);

                return new(
                    EventReceived: true,
                    received,
                    DateTimeOffset.UtcNow - started,
                    $"WinEventHook đánh thức verifier: {received!.Reason}");
            }
        }

        if (completed == uiaTask &&
            uiaAvailable)
        {
            var uiaEvent =
                await uiaTask;

            if (uiaEvent is not null)
            {
                waitCancellation.Cancel();

                Interlocked.Increment(
                    ref receivedEvents);
                Interlocked.Increment(
                    ref deliveredWakeups);
                Volatile.Write(
                    ref lastEvent,
                    uiaEvent);

                return new(
                    EventReceived: true,
                    uiaEvent,
                    DateTimeOffset.UtcNow - started,
                    $"UIA3 đánh thức verifier: {uiaEvent.Reason}");
            }
        }

        waitCancellation.Cancel();

        Interlocked.Increment(
            ref pollFallbacks);

        return new(
            EventReceived: false,
            Event: null,
            DateTimeOffset.UtcNow - started,
            "Không có WinEvent/UIA event trước poll deadline; tiếp tục polling fallback.");
    }

    private async Task<DesktopSystemEvent?> WaitForUiaEventAsync(
        string windowId,
        TimeSpan fallbackDelay,
        CancellationToken cancellationToken)
    {
        if (flaUi?.Available != true)
            return null;

        var waitMilliseconds =
            Math.Clamp(
                checked((int)Math.Ceiling(
                    fallbackDelay.TotalMilliseconds)),
                100,
                3000);

        try
        {
            var response =
                await Task.Run(
                    () => flaUi.Invoke(
                        new FlaUiAutomationRequest(
                            "wait-uia-event",
                            windowId,
                            WaitMilliseconds: waitMilliseconds)),
                    cancellationToken);

            if (!response.Success ||
                string.IsNullOrWhiteSpace(
                    response.EventKind))
            {
                return null;
            }

            var kind =
                response.EventKind.Trim().ToLowerInvariant() switch
                {
                    "uia-focus-changed" =>
                        DesktopSystemEventKinds.FocusChanged,
                    "uia-structure-changed" =>
                        DesktopSystemEventKinds.StructureChanged,
                    "uia-property-changed" =>
                        DesktopSystemEventKinds.PropertyChanged,
                    _ =>
                        DesktopSystemEventKinds.PropertyChanged
                };

            return CorrelateEvidence(
                new DesktopSystemEvent(
                    kind,
                    string.IsNullOrWhiteSpace(
                        response.EventWindowId)
                        ? windowId
                        : response.EventWindowId,
                    DateTimeOffset.UtcNow,
                    0,
                    response.Detail,
                    Source: "uia3",
                    SourceConfidence: 0.90));
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            // UIA là nguồn tăng tốc bổ sung. Lỗi bridge không được làm
            // hỏng polling/WinEvent fallback của verifier.
            return null;
        }
    }

    private DesktopSystemEvent CorrelateEvidence(
        DesktopSystemEvent next)
    {
        var family =
            GetEventFamily(
                next.Kind);

        var key =
            $"{next.WindowId}|{family}";

        var corroborated = false;
        var confidence =
            next.SourceConfidence;

        if (recentEvidence.TryGetValue(
                key,
                out var previous))
        {
            var age =
                Math.Abs(
                    (next.OccurredAtUtc -
                     previous.OccurredAtUtc)
                    .TotalMilliseconds);

            if (age <= CrossSourceCorrelationMilliseconds &&
                !previous.Source.Equals(
                    next.Source,
                    StringComparison.OrdinalIgnoreCase))
            {
                corroborated = true;
                confidence =
                    Math.Min(
                        0.99,
                        Math.Max(
                            confidence,
                            previous.SourceConfidence) +
                        0.08);
            }
        }

        var enriched =
            next with
            {
                SourceConfidence = confidence,
                Corroborated = corroborated
            };

        enriched =
            AssignBurst(
                enriched);

        recentEvidence[key] =
            enriched;

        return enriched;
    }

    private DesktopSystemEvent AssignBurst(
        DesktopSystemEvent next)
    {
        lock (burstSync)
        {
            if (recentBursts.TryGetValue(
                    next.WindowId,
                    out var previous) &&
                DesktopEventBurstPolicy.BelongsToSameBurst(
                    previous,
                    next,
                    TimeSpan.FromMilliseconds(
                        BurstGroupingMilliseconds)))
            {
                var burstId =
                    string.IsNullOrWhiteSpace(
                        previous.BurstId)
                        ? $"{previous.WindowId}-{previous.OccurredAtUtc.ToUnixTimeMilliseconds()}"
                        : previous.BurstId;

                var grouped =
                    next with
                    {
                        BurstId = burstId,
                        BurstSize =
                            Math.Max(
                                1,
                                previous.BurstSize) + 1
                    };

                recentBursts[next.WindowId] =
                    grouped;

                return grouped;
            }

            var first =
                next with
                {
                    BurstId =
                        $"{next.WindowId}-{next.OccurredAtUtc.ToUnixTimeMilliseconds()}",
                    BurstSize = 1
                };

            recentBursts[next.WindowId] =
                first;

            return first;
        }
    }

    private static string GetEventFamily(
        string kind) =>
        kind switch
        {
            DesktopSystemEventKinds.ForegroundChanged or
            DesktopSystemEventKinds.FocusChanged =>
                "focus",
            DesktopSystemEventKinds.ValueChanged or
            DesktopSystemEventKinds.PropertyChanged =>
                "value",
            DesktopSystemEventKinds.WindowCreated or
            DesktopSystemEventKinds.WindowShown or
            DesktopSystemEventKinds.StructureChanged =>
                "structure-open",
            DesktopSystemEventKinds.WindowDestroyed or
            DesktopSystemEventKinds.WindowHidden =>
                "structure-close",
            DesktopSystemEventKinds.SelectionChanged =>
                "selection",
            DesktopSystemEventKinds.WindowMovedOrResized =>
                "geometry",
            _ =>
                kind
        };

    private bool TryDequeueLatest(
        string? targetWindowId,
        out DesktopSystemEvent? result)
    {
        result = null;

        while (events.TryDequeue(
                   out var item))
        {
            _ = Interlocked.Decrement(
                ref queuedEvents);

            if (IsRelevantForTarget(
                    item,
                    targetWindowId))
            {
                result = item;
            }
        }

        if (result is not null)
        {
            while (signal.Wait(0))
            {
                // Dọn wake count cũ tương ứng với các event đã coalesce.
            }
        }

        return result is not null;
    }

    private static bool IsRelevantForTarget(
        DesktopSystemEvent item,
        string? targetWindowId)
    {
        var target =
            (targetWindowId ?? string.Empty)
                .Trim();

        if (target.Length == 0 ||
            item.WindowId.Equals(
                target,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Các thay đổi top-level vẫn có thể là kết quả mong đợi của action
        // (mở/đóng/chuyển app), nên không lọc chúng theo target cũ.
        return item.Kind is
            DesktopSystemEventKinds.ForegroundChanged or
            DesktopSystemEventKinds.WindowCreated or
            DesktopSystemEventKinds.WindowDestroyed or
            DesktopSystemEventKinds.WindowShown or
            DesktopSystemEventKinds.WindowHidden;
    }

    private void HookLoop()
    {
        hookThreadId =
            GetCurrentThreadId();

        callback = OnWinEvent;

        hook = SetWinEventHook(
            EventSystemForeground,
            EventObjectValueChange,
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

        var topLevelEvent =
            nativeEvent is
                EventSystemForeground or
                EventObjectCreate or
                EventObjectDestroy or
                EventObjectShow or
                EventObjectHide or
                EventObjectLocationChange or
                EventObjectNameChange;

        var accessibilityEvent =
            nativeEvent is
                EventObjectFocus or
                EventObjectSelection or
                EventObjectSelectionAdd or
                EventObjectSelectionRemove or
                EventObjectValueChange;

        var relevantObject =
            nativeEvent == EventSystemForeground ||
            (topLevelEvent &&
             objectId == ObjidWindow &&
             childId == 0) ||
            accessibilityEvent;

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
                EventObjectFocus =>
                    DesktopSystemEventKinds.FocusChanged,
                EventObjectSelection or
                EventObjectSelectionAdd or
                EventObjectSelectionRemove =>
                    DesktopSystemEventKinds.SelectionChanged,
                EventObjectLocationChange =>
                    DesktopSystemEventKinds.WindowMovedOrResized,
                EventObjectNameChange =>
                    DesktopSystemEventKinds.WindowNameChanged,
                EventObjectValueChange =>
                    DesktopSystemEventKinds.ValueChanged,
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
                DesktopSystemEventKinds.FocusChanged =>
                    "Phần tử accessibility đang focus vừa thay đổi.",
                DesktopSystemEventKinds.SelectionChanged =>
                    "Lựa chọn accessibility vừa thay đổi.",
                DesktopSystemEventKinds.ValueChanged =>
                    "Giá trị accessibility vừa thay đổi.",
                _ =>
                    "Windows UI state vừa thay đổi."
            };

        var next =
            CorrelateEvidence(
                new DesktopSystemEvent(
                    kind,
                    $"0x{window.ToInt64():X}",
                    DateTimeOffset.UtcNow,
                    nativeEvent,
                    reason,
                    Source: "win-event",
                    SourceConfidence:
                        kind is
                            DesktopSystemEventKinds.ForegroundChanged or
                            DesktopSystemEventKinds.WindowCreated or
                            DesktopSystemEventKinds.WindowDestroyed
                            ? 0.90
                            : 0.82));

        Interlocked.Increment(
            ref receivedEvents);

        var nowMilliseconds =
            next.OccurredAtUtc.ToUnixTimeMilliseconds();

        var identity =
            $"{next.Kind}|{next.WindowId}";

        var previousIdentity =
            Volatile.Read(
                ref lastEventIdentity);

        var previousMilliseconds =
            Interlocked.Read(
                ref lastEventUnixMilliseconds);

        if (identity.Equals(
                previousIdentity,
                StringComparison.Ordinal) &&
            nowMilliseconds - previousMilliseconds <=
                DuplicateWindowMilliseconds)
        {
            Interlocked.Increment(
                ref coalescedEvents);
            Volatile.Write(
                ref lastEvent,
                next);
            Interlocked.Exchange(
                ref lastEventUnixMilliseconds,
                nowMilliseconds);
            return;
        }

        Volatile.Write(
            ref lastEventIdentity,
            identity);
        Volatile.Write(
            ref lastEvent,
            next);
        Interlocked.Exchange(
            ref lastEventUnixMilliseconds,
            nowMilliseconds);

        while (Interlocked.Read(
                   ref queuedEvents) >= MaximumQueuedEvents &&
               events.TryDequeue(
                   out _))
        {
            _ = Interlocked.Decrement(
                ref queuedEvents);
            Interlocked.Increment(
                ref droppedEvents);
        }

        events.Enqueue(
            next);
        Interlocked.Increment(
            ref queuedEvents);

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
