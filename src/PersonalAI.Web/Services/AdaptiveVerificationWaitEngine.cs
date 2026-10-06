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
    Task<AdaptiveWaitResult> WaitWithProgressIntelligenceAsync(
        Func<CancellationToken, Task<ComputerOperatorProgressObservation>> observationProvider,
        AdaptiveWaitPolicy? policy = null,
        string? targetWindowId = null,
        string? action = null,
        string? expectedEffect = null,
        CancellationToken cancellationToken = default);

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

    Task<AdaptiveWaitResult> WaitAsync(
        Func<CancellationToken, Task<AdaptiveProgressSample>> sampleProvider,
        AdaptiveWaitPolicy? policy,
        string? targetWindowId,
        string? action,
        string? expectedEffect,
        CancellationToken cancellationToken);
}

public sealed class AdaptiveVerificationWaitEngine(
    IAdaptiveObservationWakeSource? wakeSource = null,
    IComputerOperatorProgressIntelligence? progressIntelligence = null)
    : IAdaptiveVerificationWaitEngine
{
    public Task<AdaptiveWaitResult> WaitWithProgressIntelligenceAsync(
        Func<CancellationToken, Task<ComputerOperatorProgressObservation>> observationProvider,
        AdaptiveWaitPolicy? policy = null,
        string? targetWindowId = null,
        string? action = null,
        string? expectedEffect = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observationProvider);

        var intelligence =
            progressIntelligence ??
            new ComputerOperatorProgressIntelligence(
                new ComputerOperatorRuntimeStateIntelligence());

        return WaitAsync(
            async token =>
            {
                var observation =
                    await observationProvider(token);

                return intelligence
                    .Assess(observation)
                    .AdaptiveSample;
            },
            policy,
            targetWindowId,
            action,
            expectedEffect,
            cancellationToken);
    }

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

    public Task<AdaptiveWaitResult> WaitAsync(
        Func<CancellationToken, Task<AdaptiveProgressSample>> sampleProvider,
        AdaptiveWaitPolicy? policy,
        string? targetWindowId,
        string? action,
        CancellationToken cancellationToken) =>
        WaitAsync(
            sampleProvider,
            policy,
            targetWindowId,
            action,
            expectedEffect: null,
            cancellationToken);

    public async Task<AdaptiveWaitResult> WaitAsync(
        Func<CancellationToken, Task<AdaptiveProgressSample>> sampleProvider,
        AdaptiveWaitPolicy? policy,
        string? targetWindowId,
        string? action,
        string? expectedEffect,
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
                            expectedEffect,
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
        DesktopSystemEvent? desktopEvent) =>
        Score(
            action,
            expectedEffect: null,
            desktopEvent);

    public static double Score(
        string? action,
        string? expectedEffect,
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
        var effect =
            (expectedEffect ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        var semanticBoost =
            0.0;

        if (effect.Length > 0)
        {
            if ((effect.Contains("focus") ||
                 effect.Contains("foreground")) &&
                kind is
                    DesktopSystemEventKinds.FocusChanged or
                    DesktopSystemEventKinds.ForegroundChanged)
            {
                semanticBoost = 0.20;
            }
            else if ((effect.Contains("text") ||
                      effect.Contains("type") ||
                      effect.Contains("value") ||
                      effect.Contains("nhập") ||
                      effect.Contains("gõ")) &&
                     kind is
                         DesktopSystemEventKinds.ValueChanged or
                         DesktopSystemEventKinds.PropertyChanged)
            {
                semanticBoost = 0.20;
            }
            else if ((effect.Contains("open") ||
                      effect.Contains("mở") ||
                      effect.Contains("appear") ||
                      effect.Contains("xuất hiện") ||
                      effect.Contains("menu") ||
                      effect.Contains("dialog")) &&
                     kind is
                         DesktopSystemEventKinds.WindowCreated or
                         DesktopSystemEventKinds.WindowShown or
                         DesktopSystemEventKinds.StructureChanged)
            {
                semanticBoost = 0.20;
            }
            else if ((effect.Contains("close") ||
                      effect.Contains("đóng") ||
                      effect.Contains("disappear") ||
                      effect.Contains("biến mất")) &&
                     kind is
                         DesktopSystemEventKinds.WindowDestroyed or
                         DesktopSystemEventKinds.WindowHidden)
            {
                semanticBoost = 0.20;
            }
            else if ((effect.Contains("select") ||
                      effect.Contains("chọn")) &&
                     kind == DesktopSystemEventKinds.SelectionChanged)
            {
                semanticBoost = 0.20;
            }
            else if ((effect.Contains("resize") ||
                      effect.Contains("maximize") ||
                      effect.Contains("restore") ||
                      effect.Contains("kích thước")) &&
                     kind == DesktopSystemEventKinds.WindowMovedOrResized)
            {
                semanticBoost = 0.20;
            }
        }

        if (normalizedAction is
            "type-text")
        {
            var score = kind switch
            {
                DesktopSystemEventKinds.ValueChanged => 1.0,
                DesktopSystemEventKinds.PropertyChanged => 0.95,
                DesktopSystemEventKinds.FocusChanged => 0.75,
                DesktopSystemEventKinds.SelectionChanged => 0.70,
                _ => 0.35
            };

            return ApplySourceConfidence(
                Math.Min(
                    1.0,
                    score + semanticBoost),
                desktopEvent);
        }

        if (normalizedAction is
            "focus-window")
        {
            var score = kind switch
            {
                DesktopSystemEventKinds.ForegroundChanged => 1.0,
                DesktopSystemEventKinds.FocusChanged => 0.95,
                DesktopSystemEventKinds.WindowShown => 0.75,
                _ => 0.30
            };

            return ApplySourceConfidence(
                Math.Min(
                    1.0,
                    score + semanticBoost),
                desktopEvent);
        }

        if (normalizedAction is
            "restore" or
            "maximize")
        {
            var score = kind switch
            {
                DesktopSystemEventKinds.WindowMovedOrResized => 1.0,
                DesktopSystemEventKinds.ForegroundChanged => 0.90,
                DesktopSystemEventKinds.WindowShown => 0.80,
                DesktopSystemEventKinds.PropertyChanged => 0.65,
                _ => 0.30
            };

            return ApplySourceConfidence(
                Math.Min(
                    1.0,
                    score + semanticBoost),
                desktopEvent);
        }

        if (normalizedAction is
            "press-key" or
            "press-hotkey")
        {
            var score = kind switch
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

            return ApplySourceConfidence(
                Math.Min(
                    1.0,
                    score + semanticBoost),
                desktopEvent);
        }

        if (normalizedAction is
            "structured-invoke" or
            "structured-legacy-default")
        {
            var score = kind switch
            {
                DesktopSystemEventKinds.ForegroundChanged => 1.0,
                DesktopSystemEventKinds.WindowCreated => 0.98,
                DesktopSystemEventKinds.WindowDestroyed => 0.98,
                DesktopSystemEventKinds.WindowShown => 0.95,
                DesktopSystemEventKinds.WindowHidden => 0.95,
                DesktopSystemEventKinds.StructureChanged => 0.92,
                DesktopSystemEventKinds.SelectionChanged => 0.82,
                DesktopSystemEventKinds.ValueChanged => 0.80,
                DesktopSystemEventKinds.PropertyChanged => 0.78,
                DesktopSystemEventKinds.FocusChanged => 0.72,
                _ => 0.45
            };

            return ApplySourceConfidence(
                Math.Min(
                    1.0,
                    score + semanticBoost),
                desktopEvent);
        }

        if (normalizedAction is
            "structured-set-value")
        {
            var score = kind switch
            {
                DesktopSystemEventKinds.ValueChanged => 1.0,
                DesktopSystemEventKinds.PropertyChanged => 0.95,
                DesktopSystemEventKinds.FocusChanged => 0.65,
                _ => 0.30
            };

            return ApplySourceConfidence(
                Math.Min(
                    1.0,
                    score + semanticBoost),
                desktopEvent);
        }

        if (normalizedAction is
            "structured-select")
        {
            var score = kind switch
            {
                DesktopSystemEventKinds.SelectionChanged => 1.0,
                DesktopSystemEventKinds.PropertyChanged => 0.88,
                DesktopSystemEventKinds.StructureChanged => 0.72,
                _ => 0.30
            };

            return ApplySourceConfidence(
                Math.Min(
                    1.0,
                    score + semanticBoost),
                desktopEvent);
        }

        if (normalizedAction is
            "structured-toggle" or
            "structured-expand" or
            "structured-collapse")
        {
            var score = kind switch
            {
                DesktopSystemEventKinds.PropertyChanged => 0.98,
                DesktopSystemEventKinds.ValueChanged => 0.92,
                DesktopSystemEventKinds.StructureChanged => 0.90,
                DesktopSystemEventKinds.SelectionChanged => 0.75,
                _ => 0.30
            };

            return ApplySourceConfidence(
                Math.Min(
                    1.0,
                    score + semanticBoost),
                desktopEvent);
        }

        if (normalizedAction is
            "click-left" or
            "double-click-left")
        {
            var score = kind switch
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

            return ApplySourceConfidence(
                Math.Min(
                    1.0,
                    score + semanticBoost),
                desktopEvent);
        }

        // Không biết action: ưu tiên event thay đổi trạng thái lớn,
        // nhưng không coi mọi tín hiệu nhỏ là đáng wake.
        var fallbackScore = kind switch
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

        return ApplySourceConfidence(
            Math.Min(
                1.0,
                fallbackScore +
                semanticBoost),
            desktopEvent);
    }

    private static double ApplySourceConfidence(
        double relevance,
        DesktopSystemEvent desktopEvent)
    {
        var sourceWeight =
            Math.Clamp(
                desktopEvent.SourceConfidence,
                0.50,
                1.0);

        var fused =
            relevance *
            sourceWeight;

        if (desktopEvent.Corroborated)
        {
            fused =
                Math.Min(
                    1.0,
                    fused + 0.10);
        }

        return fused;
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
            "maximize" or
            "structured-invoke" or
            "structured-legacy-default" or
            "structured-set-value" or
            "structured-select" or
            "structured-toggle" or
            "structured-expand" or
            "structured-collapse";
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
            "structured-invoke" or
            "structured-legacy-default" =>
                new(
                    TimeSpan.FromMilliseconds(220),
                    TimeSpan.FromSeconds(6),
                    TimeSpan.FromSeconds(30),
                    0.72),

            "structured-set-value" or
            "structured-select" or
            "structured-toggle" or
            "structured-expand" or
            "structured-collapse" =>
                new(
                    TimeSpan.FromMilliseconds(180),
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromSeconds(12),
                    0.72),

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
