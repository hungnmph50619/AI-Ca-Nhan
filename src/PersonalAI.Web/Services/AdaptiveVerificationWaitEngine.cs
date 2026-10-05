using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class AdaptiveWaitStatuses
{
    public const string Verified = "verified";
    public const string Progressing = "progressing";
    public const string Pending = "pending";
    public const string Stalled = "stalled";
    public const string Failed = "failed";
}

public sealed record AdaptiveWaitPolicy(
    TimeSpan PollInterval,
    TimeSpan StallTimeout,
    TimeSpan AbsoluteTimeout,
    double MinimumProgressConfidence = 0.65)
{
    public static AdaptiveWaitPolicy Default { get; } =
        new(
            PollInterval: TimeSpan.FromMilliseconds(500),
            StallTimeout: TimeSpan.FromSeconds(8),
            AbsoluteTimeout: TimeSpan.FromSeconds(120),
            MinimumProgressConfidence: 0.65);
}

public sealed record AdaptiveProgressSample(
    string Status,
    double Confidence,
    string Reason,
    bool MeaningfulProgress = false);

public sealed record AdaptiveWaitResult(
    string Status,
    bool Verified,
    bool Failed,
    int Samples,
    int ProgressHeartbeats,
    TimeSpan Elapsed,
    string Reason,
    int EventWakeups = 0,
    int IgnoredEventWakeups = 0);

public interface IAdaptiveVerificationWaitEngine
{
    Task<AdaptiveWaitResult> WaitAsync(
        Func<CancellationToken, Task<AdaptiveProgressSample>> sampleProvider,
        AdaptiveWaitPolicy? policy = null,
        CancellationToken cancellationToken = default);

    Task<AdaptiveWaitResult> WaitAsync(
        Func<CancellationToken, Task<AdaptiveProgressSample>> sampleProvider,
        AdaptiveWaitPolicy? policy,
        string? targetWindowId,
        CancellationToken cancellationToken);

    Task<AdaptiveWaitResult> WaitAsync(
        Func<CancellationToken, Task<AdaptiveProgressSample>> sampleProvider,
        AdaptiveWaitPolicy? policy,
        string? targetWindowId,
        string? action,
        CancellationToken cancellationToken);
}

public sealed class AdaptiveVerificationWaitEngine(
    IAdaptiveObservationWakeSource? wakeSource = null)
    : IAdaptiveVerificationWaitEngine
{
    public Task<AdaptiveWaitResult> WaitAsync(
        Func<CancellationToken, Task<AdaptiveProgressSample>> sampleProvider,
        AdaptiveWaitPolicy? policy = null,
        CancellationToken cancellationToken = default) =>
        WaitAsync(
            sampleProvider,
            policy,
            targetWindowId: null,
            cancellationToken);

    public Task<AdaptiveWaitResult> WaitAsync(
        Func<CancellationToken, Task<AdaptiveProgressSample>> sampleProvider,
        AdaptiveWaitPolicy? policy,
        string? targetWindowId,
        CancellationToken cancellationToken) =>
        WaitAsync(
            sampleProvider,
            policy,
            targetWindowId,
            action: null,
            cancellationToken);

    public async Task<AdaptiveWaitResult> WaitAsync(
        Func<CancellationToken, Task<AdaptiveProgressSample>> sampleProvider,
        AdaptiveWaitPolicy? policy,
        string? targetWindowId,
        string? action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sampleProvider);

        var resolved = policy ?? AdaptiveWaitPolicy.Default;

        if (resolved.PollInterval <= TimeSpan.Zero ||
            resolved.StallTimeout <= TimeSpan.Zero ||
            resolved.AbsoluteTimeout <= TimeSpan.Zero ||
            resolved.StallTimeout > resolved.AbsoluteTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                "Cấu hình Adaptive Wait không hợp lệ.");
        }

        var started = DateTimeOffset.UtcNow;
        var lastMeaningfulProgress = started;
        var samples = 0;
        var heartbeats = 0;
        var eventWakeups = 0;
        var ignoredEventWakeups = 0;
        var lastReason =
            "Chưa có bằng chứng kết quả hoặc tiến triển.";

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var now = DateTimeOffset.UtcNow;
            var elapsed = now - started;

            if (elapsed >= resolved.AbsoluteTimeout)
            {
                return new(
                    AdaptiveWaitStatuses.Stalled,
                    Verified: false,
                    Failed: false,
                    samples,
                    heartbeats,
                    elapsed,
                    $"Đã chạm giới hạn chờ tuyệt đối {resolved.AbsoluteTimeout.TotalSeconds:0}s. {lastReason}",
                    eventWakeups,
                    ignoredEventWakeups);
            }

            var sample =
                await sampleProvider(
                    cancellationToken);

            samples++;
            lastReason = sample.Reason;

            if (sample.Status.Equals(
                    AdaptiveWaitStatuses.Verified,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    AdaptiveWaitStatuses.Verified,
                    Verified: true,
                    Failed: false,
                    samples,
                    heartbeats,
                    DateTimeOffset.UtcNow - started,
                    sample.Reason,
                    eventWakeups,
                    ignoredEventWakeups);
            }

            if (sample.Status.Equals(
                    AdaptiveWaitStatuses.Failed,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    AdaptiveWaitStatuses.Failed,
                    Verified: false,
                    Failed: true,
                    samples,
                    heartbeats,
                    DateTimeOffset.UtcNow - started,
                    sample.Reason,
                    eventWakeups,
                    ignoredEventWakeups);
            }

            var strongProgress =
                sample.MeaningfulProgress &&
                sample.Confidence >=
                    resolved.MinimumProgressConfidence;

            if (strongProgress)
            {
                heartbeats++;
                lastMeaningfulProgress =
                    DateTimeOffset.UtcNow;
            }

            var stalledFor =
                DateTimeOffset.UtcNow -
                lastMeaningfulProgress;

            if (stalledFor >= resolved.StallTimeout)
            {
                return new(
                    AdaptiveWaitStatuses.Stalled,
                    Verified: false,
                    Failed: false,
                    samples,
                    heartbeats,
                    DateTimeOffset.UtcNow - started,
                    $"Không có tiến triển đủ mạnh trong {resolved.StallTimeout.TotalSeconds:0}s. {sample.Reason}",
                    eventWakeups,
                    ignoredEventWakeups);
            }

            if (wakeSource is not null)
            {
                var wake = await wakeSource.WaitForWindowAsync(
                    targetWindowId,
                    resolved.PollInterval,
                    cancellationToken);

                if (wake.EventReceived)
                {
                    var relevance =
                        ComputerOperatorEventRelevance.Score(
                            action,
                            wake.Event);

                    if (relevance >= 0.55)
                    {
                        eventWakeups++;
                    }
                    else
                    {
                        ignoredEventWakeups++;

                        // Tránh event không liên quan gây vòng lặp capture nóng.
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(
                                Math.Min(
                                    50,
                                    resolved.PollInterval.TotalMilliseconds)),
                            cancellationToken);
                    }
                }
            }
            else
            {
                await Task.Delay(
                    resolved.PollInterval,
                    cancellationToken);
            }
        }
    }
}

public static class ComputerOperatorEventRelevance
{
    public static double Score(
        string? action,
        DesktopSystemEvent? desktopEvent)
    {
        if (desktopEvent is null)
            return 0;

        var normalizedAction =
            (action ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        var kind =
            desktopEvent.Kind;

        if (normalizedAction is
            "type-text")
        {
            return kind switch
            {
                DesktopSystemEventKinds.ValueChanged => 1.0,
                DesktopSystemEventKinds.PropertyChanged => 0.95,
                DesktopSystemEventKinds.FocusChanged => 0.75,
                DesktopSystemEventKinds.SelectionChanged => 0.70,
                _ => 0.35
            };
        }

        if (normalizedAction is
            "focus-window")
        {
            return kind switch
            {
                DesktopSystemEventKinds.ForegroundChanged => 1.0,
                DesktopSystemEventKinds.FocusChanged => 0.95,
                DesktopSystemEventKinds.WindowShown => 0.75,
                _ => 0.30
            };
        }

        if (normalizedAction is
            "restore" or
            "maximize")
        {
            return kind switch
            {
                DesktopSystemEventKinds.WindowMovedOrResized => 1.0,
                DesktopSystemEventKinds.ForegroundChanged => 0.90,
                DesktopSystemEventKinds.WindowShown => 0.80,
                DesktopSystemEventKinds.PropertyChanged => 0.65,
                _ => 0.30
            };
        }

        if (normalizedAction is
            "press-key" or
            "press-hotkey")
        {
            return kind switch
            {
                DesktopSystemEventKinds.ForegroundChanged => 1.0,
                DesktopSystemEventKinds.WindowCreated => 0.95,
                DesktopSystemEventKinds.WindowShown => 0.95,
                DesktopSystemEventKinds.FocusChanged => 0.85,
                DesktopSystemEventKinds.ValueChanged => 0.80,
                DesktopSystemEventKinds.SelectionChanged => 0.75,
                DesktopSystemEventKinds.StructureChanged => 0.75,
                DesktopSystemEventKinds.PropertyChanged => 0.70,
                _ => 0.45
            };
        }

        if (normalizedAction is
            "click-left" or
            "double-click-left")
        {
            return kind switch
            {
                DesktopSystemEventKinds.ForegroundChanged => 1.0,
                DesktopSystemEventKinds.WindowCreated => 0.95,
                DesktopSystemEventKinds.WindowDestroyed => 0.95,
                DesktopSystemEventKinds.WindowShown => 0.90,
                DesktopSystemEventKinds.WindowHidden => 0.90,
                DesktopSystemEventKinds.StructureChanged => 0.90,
                DesktopSystemEventKinds.SelectionChanged => 0.85,
                DesktopSystemEventKinds.ValueChanged => 0.80,
                DesktopSystemEventKinds.PropertyChanged => 0.75,
                DesktopSystemEventKinds.FocusChanged => 0.70,
                _ => 0.50
            };
        }

        // Không biết action: ưu tiên event thay đổi trạng thái lớn,
        // nhưng không coi mọi tín hiệu nhỏ là đáng wake.
        return kind switch
        {
            DesktopSystemEventKinds.ForegroundChanged => 0.95,
            DesktopSystemEventKinds.WindowCreated => 0.90,
            DesktopSystemEventKinds.WindowDestroyed => 0.90,
            DesktopSystemEventKinds.WindowShown => 0.85,
            DesktopSystemEventKinds.WindowHidden => 0.85,
            DesktopSystemEventKinds.StructureChanged => 0.70,
            DesktopSystemEventKinds.PropertyChanged => 0.65,
            _ => 0.50
        };
    }
}

public static class ComputerOperatorAdaptiveWaitPolicy
{
    public static bool SupportsAdaptiveWaiting(
        string? action)
    {
        var normalized =
            (action ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        return normalized is
            "click-left" or
            "double-click-left" or
            "press-key" or
            "press-hotkey" or
            "focus-window" or
            "restore" or
            "maximize";
    }

    public static AdaptiveWaitPolicy ForAction(
        string? action)
    {
        var normalized =
            (action ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        return normalized switch
        {
            "click-left" or
            "double-click-left" =>
                new(
                    TimeSpan.FromMilliseconds(500),
                    TimeSpan.FromSeconds(20),
                    TimeSpan.FromSeconds(120),
                    0.70),

            "focus-window" or
            "restore" or
            "maximize" =>
                new(
                    TimeSpan.FromMilliseconds(250),
                    TimeSpan.FromSeconds(4),
                    TimeSpan.FromSeconds(20),
                    0.70),

            _ =>
                new(
                    TimeSpan.FromMilliseconds(350),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(30),
                    0.70)
        };
    }

    public static AdaptiveProgressSample FromDesktopObservation(
        DesktopFastObservation observation,
        DesktopFrameDifference? difference,
        DesktopVerificationRoutingResult route)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(route);

        if (route.Route ==
            DesktopVerificationRoute.LocalVerified)
        {
            return new(
                AdaptiveWaitStatuses.Verified,
                route.Confidence,
                route.Reason,
                MeaningfulProgress: true);
        }

        // Không coi LocalFailed tức thời là thất bại cuối cùng nếu action
        // có thể kích hoạt transition chậm. Đây chỉ là "chưa thấy kết quả".
        var foregroundProgress =
            observation.ForegroundWindowChanged;

        var boundsProgress =
            observation.WindowBoundsChanged;

        var strongVisualProgress =
            difference?.Comparable == true &&
            difference.ChangedRatio >= 0.03;

        var mediumVisualProgress =
            difference?.Comparable == true &&
            difference.ChangedRatio >= 0.01;

        if (foregroundProgress)
        {
            return new(
                AdaptiveWaitStatuses.Progressing,
                0.95,
                "Foreground window đã thay đổi; có bằng chứng mạnh rằng ứng dụng đang chuyển trạng thái.",
                MeaningfulProgress: true);
        }

        if (boundsProgress)
        {
            return new(
                AdaptiveWaitStatuses.Progressing,
                0.88,
                "Hình học cửa sổ đã thay đổi; transition đang tiến triển.",
                MeaningfulProgress: true);
        }

        if (strongVisualProgress)
        {
            return new(
                AdaptiveWaitStatuses.Progressing,
                0.60,
                $"Frame thay đổi {difference!.ChangedRatio * 100:0.00}%; ghi nhận thay đổi nhưng chưa đủ chắc để gia hạn stall timer.",
                MeaningfulProgress: false);
        }

        if (mediumVisualProgress)
        {
            return new(
                AdaptiveWaitStatuses.Progressing,
                0.55,
                $"Frame thay đổi {difference!.ChangedRatio * 100:0.00}% nhưng chưa đủ mạnh để gia hạn stall timer.",
                MeaningfulProgress: false);
        }

        return new(
            AdaptiveWaitStatuses.Pending,
            0.30,
            "Chưa có bằng chứng kết quả cuối cùng; tiếp tục quan sát mà không thực thi lại action.",
            MeaningfulProgress: false);
    }
}
