using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public enum DesktopVerificationRoute
{
    LocalVerified,
    LocalFailed,
    GeminiRequired
}

public sealed record DesktopVerificationRoutingResult(
    DesktopVerificationRoute Route,
    double Confidence,
    string Reason);

public interface IDesktopVerificationRouter
{
    DesktopVerificationRoutingResult Route(
        DesktopOperatorDecision decision,
        DesktopFastObservation observation,
        DesktopFrameDifference? frameDifference);
}

public sealed class DesktopVerificationRouter
    : IDesktopVerificationRouter
{
    private const double StrongVisualChangeRatio = 0.015;
    private const double NoVisualChangeRatio = 0.001;

    public DesktopVerificationRoutingResult Route(
        DesktopOperatorDecision decision,
        DesktopFastObservation observation,
        DesktopFrameDifference? frameDifference)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(observation);

        var action = (decision.Action ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if ((action is
                 "click-left" or
                 "double-click-left" or
                 "press-key" or
                 "press-hotkey") &&
            IsLaunchTransitionExpected(
                decision))
        {
            if (IsBrowserProcess(
                    observation.ActiveProcessName) &&
                !LooksLikeBrowserTarget(
                    decision))
            {
                return new(
                    DesktopVerificationRoute.LocalFailed,
                    0.99,
                    $"Launch identity mismatch: foreground sau action là browser process '{observation.ActiveProcessName}' nhưng target/expected effect không phải browser hoặc web. Không được xác nhận native app launch chỉ dựa vào window title.");
            }

            var launchTransition =
                observation.ForegroundWindowChanged &&
                (observation.WindowBoundsChanged ||
                 (frameDifference?.Comparable == true &&
                  frameDifference.ChangedRatio >=
                      StrongVisualChangeRatio));

            if (launchTransition)
            {
                var identity =
                    AssessExpectedApplicationIdentity(
                        decision,
                        observation);

                if (!identity.Matched)
                {
                    return new(
                        DesktopVerificationRoute.GeminiRequired,
                        0.0,
                        $"Launch transition đã xảy ra nhưng app identity chưa được local evidence xác nhận. {identity.Reason}");
                }

                return new(
                    DesktopVerificationRoute.LocalVerified,
                    0.98,
                    $"Expected effect là khởi chạy/mở đúng ứng dụng; foreground transition và app identity cùng khớp. {identity.Reason}");
            }

            if (frameDifference?.Comparable == true &&
                frameDifference.ChangedRatio <=
                    NoVisualChangeRatio &&
                !observation.ForegroundWindowChanged)
            {
                return new(
                    DesktopVerificationRoute.GeminiRequired,
                    0.0,
                    "Expected effect là khởi chạy ứng dụng nhưng chưa thấy transition; không kết luận action-no-effect ngay vì ứng dụng có thể đang tải.");
            }
        }

        if (action is "press-key" or "press-hotkey")
        {
            var strongTransition =
                observation.ForegroundWindowChanged &&
                (observation.WindowBoundsChanged ||
                 (frameDifference?.Comparable == true &&
                  frameDifference.ChangedRatio >= StrongVisualChangeRatio));

            if (strongTransition)
            {
                return new(
                    DesktopVerificationRoute.LocalVerified,
                    0.96,
                    "Thao tác bàn phím tạo transition desktop rõ ràng: foreground đổi và có thêm bằng chứng hình học/hình ảnh.");
            }

            if (frameDifference?.Comparable == true &&
                frameDifference.ChangedRatio <= NoVisualChangeRatio &&
                !observation.ForegroundWindowChanged)
            {
                return new(
                    DesktopVerificationRoute.LocalFailed,
                    0.94,
                    "Thao tác bàn phím không tạo transition desktop quan sát được.");
            }
        }

        if (observation.TargetLikelyOccluded ||
            observation.TargetMissing)
        {
            return new(
                DesktopVerificationRoute.GeminiRequired,
                0.0,
                "Target có dấu hiệu biến mất hoặc bị che; cần Vision hiểu lại ngữ cảnh.");
        }

        if (action is "maximize" or "restore" or "minimize")
        {
            if (observation.WindowBoundsChanged ||
                observation.ForegroundWindowChanged)
            {
                return new(
                    DesktopVerificationRoute.LocalVerified,
                    0.98,
                    "Local verifier xác nhận trạng thái cửa sổ đã thay đổi sau thao tác.");
            }

            if (frameDifference?.Comparable == true &&
                frameDifference.ChangedRatio <= NoVisualChangeRatio)
            {
                return new(
                    DesktopVerificationRoute.LocalFailed,
                    0.96,
                    "Không thấy thay đổi hình học cửa sổ hoặc hình ảnh sau thao tác cửa sổ.");
            }
        }

        if (action == "focus-window")
        {
            if (observation.ForegroundWindowChanged)
            {
                var identity =
                    AssessExpectedApplicationIdentity(
                        decision,
                        observation);

                if (identity.Matched)
                {
                    return new(
                        DesktopVerificationRoute.LocalVerified,
                        0.98,
                        $"Foreground đã đổi sang đúng app/window identity mong đợi. {identity.Reason}");
                }

                return new(
                    DesktopVerificationRoute.GeminiRequired,
                    0.0,
                    $"Foreground đã đổi nhưng local evidence chưa xác nhận đúng app/window identity đích. {identity.Reason}");
            }

            return new(
                DesktopVerificationRoute.GeminiRequired,
                0.0,
                "Foreground không đổi; cần Vision xác minh cửa sổ đích có thể đã được focus sẵn.");
        }

        if (action is
            "structured-invoke" or
            "structured-legacy-default")
        {
            var strongTransition =
                observation.ForegroundWindowChanged ||
                observation.WindowBoundsChanged ||
                (frameDifference?.Comparable == true &&
                 frameDifference.ChangedRatio >= StrongVisualChangeRatio);

            if (strongTransition)
            {
                return new(
                    DesktopVerificationRoute.LocalVerified,
                    0.95,
                    "Structured invoke tạo transition desktop quan sát được; có thể xác minh local sau khi event wake/correlation đã dẫn tới observation mới.");
            }

            if (frameDifference?.Comparable == true &&
                frameDifference.ChangedRatio <= NoVisualChangeRatio &&
                !observation.ForegroundWindowChanged &&
                !observation.WindowBoundsChanged)
            {
                return new(
                    DesktopVerificationRoute.GeminiRequired,
                    0.0,
                    "Structured invoke chưa tạo transition local đủ rõ; không kết luận fail ngay, tiếp tục adaptive wait/semantic verification.");
            }
        }

        if (action is
            "structured-toggle" or
            "structured-select" or
            "structured-expand" or
            "structured-collapse" or
            "structured-set-value")
        {
            // Các action này ưu tiên StructuredDesktopVerificationService.
            // Nếu đã rơi xuống router nghĩa là structured evidence chưa đủ.
            if (frameDifference?.Comparable == true &&
                frameDifference.ChangedRatio >= StrongVisualChangeRatio)
            {
                return new(
                    DesktopVerificationRoute.LocalVerified,
                    0.90,
                    "Structured state chưa đủ nhưng UI có thay đổi local rõ ràng; coi đây là bằng chứng phụ, vẫn qua Evidence Fusion trước khi complete.");
            }
        }

        if (action == "scroll")
        {
            if (frameDifference?.Comparable == true &&
                frameDifference.ChangedRatio >= StrongVisualChangeRatio)
            {
                return new(
                    DesktopVerificationRoute.LocalVerified,
                    0.94,
                    $"Frame thay đổi {frameDifference.ChangedRatio * 100:0.00}% sau thao tác cuộn.");
            }

            if (frameDifference?.Comparable == true &&
                frameDifference.ChangedRatio <= NoVisualChangeRatio)
            {
                return new(
                    DesktopVerificationRoute.LocalFailed,
                    0.93,
                    "Không phát hiện thay đổi hình ảnh đáng kể sau thao tác cuộn.");
            }
        }

        return new(
            DesktopVerificationRoute.GeminiRequired,
            0.0,
            "Kết quả cần hiểu semantic; chuyển sang Gemini Vision.");
    }

    internal static bool HasExpectedApplicationIdentityMatchForAcceptance(
        DesktopOperatorDecision decision,
        string? activeProcessName,
        string? activeWindowTitle) =>
        AssessExpectedApplicationIdentity(
            decision,
            new DesktopFastObservation(
                ScreenChanged: true,
                ChangeRatio: 0.1,
                ForegroundWindowChanged: true,
                WindowBoundsChanged: true,
                CursorMoved: false,
                MonitorChanged: false,
                DpiChanged: false,
                TargetMoved: false,
                TargetMissing: false,
                TargetLikelyOccluded: false,
                Summary: "acceptance",
                ActiveProcessName: activeProcessName,
                ActiveWindowTitle: activeWindowTitle))
            .Matched;

    private static ApplicationIdentityAssessment AssessExpectedApplicationIdentity(
        DesktopOperatorDecision decision,
        DesktopFastObservation observation)
    {
        var expectedText =
            NormalizeIdentityText(
                $"{decision.TargetLabel} {decision.Query} {decision.CurrentSubgoal} {decision.ExpectedEffect}");

        var identityTokens =
            expectedText
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Where(token =>
                    token.Length >= 3 &&
                    !IdentityStopWords.Contains(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (identityTokens.Length == 0)
        {
            return new(
                Matched: false,
                "Không rút ra được app identity cụ thể từ target/intent/expected effect; cần semantic verification.");
        }

        var actualIdentity =
            NormalizeIdentityText(
                $"{observation.ActiveProcessName} {observation.ActiveWindowTitle}");

        var matched =
            identityTokens
                .Where(token =>
                    actualIdentity.Contains(
                        token,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

        if (matched.Length > 0)
        {
            return new(
                Matched: true,
                $"Identity token khớp foreground hiện tại: {string.Join(",", matched.Take(3))}; process={observation.ActiveProcessName ?? "-"}; title={observation.ActiveWindowTitle ?? "-"}.");
        }

        return new(
            Matched: false,
            $"Expected identity tokens=[{string.Join(",", identityTokens.Take(6))}] không khớp foreground process/title hiện tại ({observation.ActiveProcessName ?? "-"} / {observation.ActiveWindowTitle ?? "-"}).");
    }

    private static string NormalizeIdentityText(
        string? value)
    {
        var source =
            (value ?? string.Empty)
                .Normalize(
                    System.Text.NormalizationForm.FormD);

        var builder =
            new System.Text.StringBuilder(
                source.Length);

        foreach (var character in source)
        {
            var category =
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(
                    character);

            if (category ==
                System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(
                char.IsLetterOrDigit(character)
                    ? char.ToLowerInvariant(character)
                    : ' ');
        }

        return builder
            .ToString()
            .Normalize(
                System.Text.NormalizationForm.FormC);
    }

    private static readonly HashSet<string> IdentityStopWords =
        new(
            new[]
            {
                "app", "application", "window", "windows", "foreground",
                "launch", "start", "open", "focus", "focused",
                "search", "result", "results", "keyword",
                "the", "and", "from", "into", "with", "for", "then",
                "ung", "dung", "cua", "so", "mo", "khoi", "chay",
                "tim", "kiem", "ket", "qua", "tu", "khoa", "phu",
                "hop", "voi", "sau", "tren", "da", "duoc", "dung"
            },
            StringComparer.OrdinalIgnoreCase);

    private sealed record ApplicationIdentityAssessment(
        bool Matched,
        string Reason);

    internal static bool IsBrowserLaunchMismatchForAcceptance(
        DesktopOperatorDecision decision,
        string? activeProcessName) =>
        IsLaunchTransitionExpected(
            decision) &&
        IsBrowserProcess(
            activeProcessName) &&
        !LooksLikeBrowserTarget(
            decision);

    private static bool IsBrowserProcess(
        string? processName)
    {
        var normalized =
            (processName ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        return normalized is
            "msedge" or
            "chrome" or
            "firefox" or
            "opera" or
            "brave" or
            "vivaldi" or
            "chromium";
    }

    private static bool LooksLikeBrowserTarget(
        DesktopOperatorDecision decision)
    {
        var text =
            $"{decision.TargetLabel} {decision.Query} {decision.Url} {decision.CurrentSubgoal} {decision.ExpectedEffect} {decision.Reason}"
                .ToLowerInvariant();

        var markers =
            new[]
            {
                "browser",
                "trình duyệt",
                "trinh duyet",
                "website",
                "web page",
                "webpage",
                "http://",
                "https://",
                "chrome",
                "edge",
                "firefox",
                "opera",
                "brave"
            };

        return markers.Any(marker =>
            text.Contains(
                marker,
                StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsLaunchTransitionExpectedForAcceptance(
        DesktopOperatorDecision decision) =>
        IsLaunchTransitionExpected(
            decision);

    private static bool IsLaunchTransitionExpected(
        DesktopOperatorDecision decision)
    {
        var text =
            $"{decision.CurrentSubgoal} {decision.ExpectedEffect} {decision.Reason}"
                .ToLowerInvariant();

        var markers =
            new[]
            {
                "khởi chạy",
                "khoi chay",
                "mở ứng dụng",
                "mo ung dung",
                "bắt đầu mở",
                "bat dau mo",
                "launch application",
                "launch app",
                "start application",
                "start app"
            };

        return markers.Any(marker =>
            text.Contains(
                marker,
                StringComparison.OrdinalIgnoreCase));
    }
}
