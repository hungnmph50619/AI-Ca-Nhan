using System.Diagnostics;
using PersonalAI.Web.Evaluation.Regression;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerOperatorRegressionLabService
{
    ComputerOperatorRegressionLabReport Run();
}

public sealed class ComputerOperatorRegressionLabService(
    IComputerOperatorAcceptanceService acceptance)
    : IComputerOperatorRegressionLabService
{
    public ComputerOperatorRegressionLabReport Run()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var results = new List<ComputerOperatorRegressionCaseResult>();

        RunCase(
            results,
            "windows-search-process-identity",
            "Windows Search dùng process identity thay vì title mơ hồ",
            () =>
            {
                var searchHost = new ComputerWindowInfo(
                    "search",
                    "Search",
                    "SearchHost",
                    1,
                    true,
                    0,
                    0,
                    800,
                    600);

                var browser = new ComputerWindowInfo(
                    "edge",
                    "Search",
                    "msedge",
                    2,
                    true,
                    0,
                    0,
                    800,
                    600);

                return
                    ComputerOperatorTaskService.IsDeterministicTextSurfaceForAcceptance(
                        searchHost) &&
                    !ComputerOperatorTaskService.IsDeterministicTextSurfaceForAcceptance(
                        browser)
                        ? "Đạt."
                        : "Windows Search classifier bị hồi quy.";
            });

        RunCase(
            results,
            "native-launch-browser-mismatch",
            "Native app launch không chấp nhận browser foreground",
            () =>
            {
                var decision = NativeLaunchDecision();

                return DesktopVerificationRouter.IsBrowserLaunchMismatchForAcceptance(
                        decision,
                        "msedge")
                    ? "Đạt."
                    : "Browser mismatch không bị chặn.";
            });

        RunCase(
            results,
            "browser-launch-not-false-positive",
            "Browser target thật không bị reject nhầm",
            () =>
            {
                var decision = NativeLaunchDecision() with
                {
                    CurrentSubgoal = "Mở trình duyệt Edge",
                    ExpectedEffect = "Trình duyệt Edge được mở.",
                    Reason = "Mở browser theo yêu cầu."
                };

                return !DesktopVerificationRouter.IsBrowserLaunchMismatchForAcceptance(
                        decision,
                        "msedge")
                    ? "Đạt."
                    : "Browser target hợp lệ bị reject nhầm.";
            });

        RunCase(
            results,
            "provider-cooldown-after-failures",
            "Provider lỗi lặp phải vào cooldown",
            () =>
            {
                var health = new LocalVisualProviderHealthRegistry();

                for (var i = 0; i < 3; i++)
                {
                    health.RecordFailure(
                        "Gemini",
                        100,
                        "regression failure");
                }

                var skipped = health.ShouldSkip(
                    "Gemini",
                    DateTimeOffset.UtcNow,
                    out var reason);

                var snapshot = health.Get("Gemini");

                return skipped &&
                       snapshot.ConsecutiveFailures == 3 &&
                       snapshot.State == LocalVisualProviderHealthState.Unavailable &&
                       !string.IsNullOrWhiteSpace(reason)
                    ? "Đạt."
                    : "Provider health không cooldown đúng sau 3 failure.";
            });

        RunCase(
            results,
            "provider-health-reset-after-success",
            "Provider success phải reset failure streak",
            () =>
            {
                var health = new LocalVisualProviderHealthRegistry();

                health.RecordFailure(
                    "OpenAI",
                    100,
                    "regression failure");

                health.RecordSuccess(
                    "OpenAI",
                    40,
                    "regression success");

                var snapshot = health.Get("OpenAI");

                return snapshot.ConsecutiveFailures == 0 &&
                       snapshot.State == LocalVisualProviderHealthState.Healthy &&
                       snapshot.LastLatencyMilliseconds == 40
                    ? "Đạt."
                    : "Provider health không reset sau success.";
            });

        RunCase(
            results,
            "legacy-acceptance-suite",
            "Toàn bộ Computer Operator acceptance suite cũ vẫn pass",
            () =>
            {
                var report = acceptance.Run();

                return report.Passed
                    ? $"Đạt {report.PassedCount}/{report.TotalCount} checks."
                    : $"Acceptance suite fail: {report.PassedCount}/{report.TotalCount} checks.";
            },
            passPredicate: detail =>
                detail.StartsWith(
                    "Đạt",
                    StringComparison.OrdinalIgnoreCase));

        stopwatch.Stop();
        var completedAt = DateTimeOffset.UtcNow;
        var passed = results.Count(item => item.Passed);

        return new ComputerOperatorRegressionLabReport(
            PersonalAiRelease.Version,
            startedAt,
            completedAt,
            results.Count,
            passed,
            results.Count - passed,
            results.Count == 0
                ? 0
                : (double)passed / results.Count,
            stopwatch.Elapsed.TotalMilliseconds,
            results);
    }

    private static void RunCase(
        ICollection<ComputerOperatorRegressionCaseResult> results,
        string id,
        string title,
        Func<string> execute,
        Func<string, bool>? passPredicate = null)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var detail = execute();
            stopwatch.Stop();

            var passed =
                passPredicate?.Invoke(detail) ??
                detail.Equals(
                    "Đạt.",
                    StringComparison.OrdinalIgnoreCase);

            results.Add(
                new ComputerOperatorRegressionCaseResult(
                    id,
                    title,
                    passed,
                    detail,
                    stopwatch.Elapsed.TotalMilliseconds));
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            results.Add(
                new ComputerOperatorRegressionCaseResult(
                    id,
                    title,
                    false,
                    $"{exception.GetType().Name}: {exception.Message}",
                    stopwatch.Elapsed.TotalMilliseconds));
        }
    }

    private static DesktopOperatorDecision NativeLaunchDecision() =>
        new(
            State: "search-results",
            Plan: "Mở ứng dụng.",
            CurrentSubgoal: "Khởi chạy ứng dụng League of Legends",
            GoalProgress: 0.2,
            VerifiedMilestones: Array.Empty<string>(),
            Action: "click",
            Query: string.Empty,
            Text: string.Empty,
            Key: string.Empty,
            Keys: Array.Empty<string>(),
            Url: string.Empty,
            TargetLabel: "League of Legends",
            CoordinateSpace: "image-pixel",
            CoordinateWindowId: string.Empty,
            ImageX: 100,
            ImageY: 100,
            EndImageX: 0,
            EndImageY: 0,
            NormalizedX: 0,
            NormalizedY: 0,
            EndNormalizedX: 0,
            EndNormalizedY: 0,
            BoxLeft: 80,
            BoxTop: 80,
            BoxWidth: 100,
            BoxHeight: 40,
            BoxNormalizedLeft: 0,
            BoxNormalizedTop: 0,
            BoxNormalizedWidth: 0,
            BoxNormalizedHeight: 0,
            ScrollDelta: 0,
            ExpectedEffect: "Ứng dụng League of Legends được khởi chạy.",
            Confidence: 0.95,
            Reason: "Khởi chạy native application.",
            SceneElements: Array.Empty<DesktopSceneElement>(),
            TargetElementId: string.Empty);
}
