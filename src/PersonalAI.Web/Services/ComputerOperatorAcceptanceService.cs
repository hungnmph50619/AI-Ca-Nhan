using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorAcceptanceCheck(
    string Name,
    bool Passed,
    string Detail);

public sealed record ComputerOperatorAcceptanceResponse(
    string Version,
    bool Passed,
    int PassedCount,
    int TotalCount,
    IReadOnlyList<ComputerOperatorAcceptanceCheck> Checks);

public interface IComputerOperatorAcceptanceService
{
    ComputerOperatorAcceptanceResponse Run();
}

public sealed class ComputerOperatorAcceptanceService
    : IComputerOperatorAcceptanceService
{
    public ComputerOperatorAcceptanceResponse Run()
    {
        var checks = new List<ComputerOperatorAcceptanceCheck>();

        RunCheck(
            checks,
            "tọa độ pixel trên desktop ảo có gốc âm",
            CheckImagePixelWithNegativeVirtualOrigin);

        RunCheck(
            checks,
            "tọa độ chuẩn hóa trong cửa sổ",
            CheckWindowNormalizedCoordinates);

        RunCheck(
            checks,
            "bounding box pixel chọn điểm click an toàn",
            CheckPixelSafeTarget);

        RunCheck(
            checks,
            "bounding box chuẩn hóa chọn điểm click an toàn",
            CheckNormalizedSafeTarget);

        RunCheck(
            checks,
            "bộ nhớ recovery chặn lặp chiến lược thất bại",
            CheckRecoveryMemory);

        RunCheck(
            checks,
            "loop guard phát hiện trạng thái đứng yên",
            CheckLoopStagnation);

        RunCheck(
            checks,
            "loop guard phát hiện dao động A-B",
            CheckLoopOscillation);

        var passed = checks.Count(item => item.Passed);

        return new(
            PersonalAiRelease.Version,
            passed == checks.Count,
            passed,
            checks.Count,
            checks);
    }

    private static void RunCheck(
        ICollection<ComputerOperatorAcceptanceCheck> checks,
        string name,
        Action check)
    {
        try
        {
            check();
            checks.Add(
                new(
                    name,
                    true,
                    "Đạt."));
        }
        catch (Exception exception)
        {
            checks.Add(
                new(
                    name,
                    false,
                    exception.Message));
        }
    }

    private static void CheckImagePixelWithNegativeVirtualOrigin()
    {
        var computer = new AcceptanceComputerUseService();
        var displays = new AcceptanceDisplayTopologyService();
        var transform = new ComputerCoordinateTransformService(
            computer,
            displays);

        var frame = new DesktopScreenshotFrame(
            Array.Empty<byte>(),
            -1920,
            0,
            3840,
            1080,
            DateTimeOffset.UtcNow);

        var point = transform.ToDesktopPoint(
            new(
                ComputerCoordinateSpaces.ImagePixel,
                100,
                50,
                0,
                0,
                string.Empty),
            frame);

        Require(
            point.DesktopX == -1820 &&
            point.DesktopY == 50,
            $"Sai chuyển đổi pixel: ({point.DesktopX},{point.DesktopY}).");

        Require(
            point.MonitorDevice == "LEFT",
            "Không gắn đúng metadata màn hình trái.");
    }

    private static void CheckWindowNormalizedCoordinates()
    {
        var computer = new AcceptanceComputerUseService();
        var transform = new ComputerCoordinateTransformService(
            computer,
            new AcceptanceDisplayTopologyService());

        var frame = new DesktopScreenshotFrame(
            Array.Empty<byte>(),
            -1920,
            0,
            3840,
            1080,
            DateTimeOffset.UtcNow);

        var point = transform.ToDesktopPoint(
            new(
                ComputerCoordinateSpaces.WindowNormalized,
                0,
                0,
                0.5,
                0.5,
                AcceptanceComputerUseService.WindowId),
            frame);

        Require(
            point.DesktopX is >= 699 and <= 700 &&
            point.DesktopY is >= 499 and <= 500,
            $"Sai tọa độ giữa cửa sổ: ({point.DesktopX},{point.DesktopY}).");
    }

    private static void CheckPixelSafeTarget()
    {
        var transform = new ComputerCoordinateTransformService(
            new AcceptanceComputerUseService(),
            new AcceptanceDisplayTopologyService());
        var targeting = new ComputerSafeTargetingService(
            transform);

        var frame = new DesktopScreenshotFrame(
            Array.Empty<byte>(),
            -1920,
            0,
            3840,
            1080,
            DateTimeOffset.UtcNow);

        var decision = BuildClickDecision(
            ComputerCoordinateSpaces.ImagePixel,
            boxLeft: 2000,
            boxTop: 300,
            boxWidth: 100,
            boxHeight: 60);

        var target = targeting.Resolve(
            decision,
            frame);

        Require(
            target.UsedBoundingBox,
            "Safe targeting không dùng bounding box.");

        var imageX = target.Point.DesktopX - frame.Left;
        var imageY = target.Point.DesktopY - frame.Top;

        Require(
            imageX > decision.BoxLeft &&
            imageX < decision.BoxLeft + decision.BoxWidth &&
            imageY > decision.BoxTop &&
            imageY < decision.BoxTop + decision.BoxHeight,
            "Điểm click an toàn nằm ngoài bounding box.");
    }

    private static void CheckNormalizedSafeTarget()
    {
        var transform = new ComputerCoordinateTransformService(
            new AcceptanceComputerUseService(),
            new AcceptanceDisplayTopologyService());
        var targeting = new ComputerSafeTargetingService(
            transform);

        var frame = new DesktopScreenshotFrame(
            Array.Empty<byte>(),
            -1920,
            0,
            3840,
            1080,
            DateTimeOffset.UtcNow);

        var decision = BuildClickDecision(
            ComputerCoordinateSpaces.WindowNormalized,
            windowId: AcceptanceComputerUseService.WindowId,
            normalizedX: 0.3,
            normalizedY: 0.4,
            boxNormalizedLeft: 0.2,
            boxNormalizedTop: 0.3,
            boxNormalizedWidth: 0.2,
            boxNormalizedHeight: 0.2);

        var target = targeting.Resolve(
            decision,
            frame);

        var window = new AcceptanceComputerUseService()
            .GetWindows()
            .Windows
            .Single();

        Require(
            target.Point.DesktopX >= window.Left &&
            target.Point.DesktopX < window.Left + window.Width &&
            target.Point.DesktopY >= window.Top &&
            target.Point.DesktopY < window.Top + window.Height,
            "Điểm click chuẩn hóa không nằm trong cửa sổ đích.");
    }

    private static void CheckRecoveryMemory()
    {
        var recovery = new ComputerOperatorRecoverySession();

        var count = recovery.RecordFailure(
            "click-left:image-pixel:100,100",
            "click-left",
            ComputerOperatorFailureKinds.VerificationFailed,
            "Start Menu chưa mở",
            "Click không tạo trạng thái mong đợi.",
            "Start Menu xuất hiện",
            0.92);

        Require(
            count == 1,
            "Bộ nhớ recovery đếm lần thất bại đầu tiên sai.");

        var avoid = recovery.ShouldAvoidRepeatedStrategy(
            "click-left:image-pixel:100,100",
            "Start Menu chưa mở",
            out var reason);

        Require(
            avoid &&
            !string.IsNullOrWhiteSpace(reason),
            "Recovery không chặn chiến lược vừa thất bại trong trạng thái tương đương.");
    }

    private static void CheckLoopStagnation()
    {
        var loop = new ComputerOperatorLoopGuardSession();
        ComputerOperatorLoopAssessment result = new(
            false,
            string.Empty,
            string.Empty,
            0);

        for (var index = 0; index < 4; index++)
            result = loop.Observe(
                "Cửa sổ không thay đổi");

        Require(
            result.Detected &&
            result.Kind == "stagnation",
            "Không phát hiện chuỗi trạng thái đứng yên 4 vòng.");
    }

    private static void CheckLoopOscillation()
    {
        var loop = new ComputerOperatorLoopGuardSession();

        _ = loop.Observe("Trạng thái A");
        _ = loop.Observe("Trạng thái B");
        _ = loop.Observe("Trạng thái A");
        var result = loop.Observe("Trạng thái B");

        Require(
            result.Detected &&
            result.Kind == "oscillation",
            "Không phát hiện dao động A-B-A-B.");

        loop.MarkProgress();
        var afterProgress = loop.Observe(
            "Trạng thái C");

        Require(
            !afterProgress.Detected,
            "Loop guard không reset sau khi xác nhận có tiến triển.");
    }

    private static DesktopOperatorDecision BuildClickDecision(
        string space,
        string windowId = "",
        int boxLeft = 0,
        int boxTop = 0,
        int boxWidth = 0,
        int boxHeight = 0,
        double normalizedX = 0,
        double normalizedY = 0,
        double boxNormalizedLeft = 0,
        double boxNormalizedTop = 0,
        double boxNormalizedWidth = 0,
        double boxNormalizedHeight = 0) =>
        new(
            State: "test",
            Plan: "test",
            CurrentSubgoal: "test",
            GoalProgress: 0.5,
            VerifiedMilestones: Array.Empty<string>(),
            Action: "click-left",
            Query: string.Empty,
            Text: string.Empty,
            Key: string.Empty,
            Keys: Array.Empty<string>(),
            Url: string.Empty,
            TargetLabel: "test-target",
            CoordinateSpace: space,
            CoordinateWindowId: windowId,
            ImageX: 0,
            ImageY: 0,
            EndImageX: 0,
            EndImageY: 0,
            NormalizedX: normalizedX,
            NormalizedY: normalizedY,
            EndNormalizedX: 0,
            EndNormalizedY: 0,
            BoxLeft: boxLeft,
            BoxTop: boxTop,
            BoxWidth: boxWidth,
            BoxHeight: boxHeight,
            BoxNormalizedLeft: boxNormalizedLeft,
            BoxNormalizedTop: boxNormalizedTop,
            BoxNormalizedWidth: boxNormalizedWidth,
            BoxNormalizedHeight: boxNormalizedHeight,
            ScrollDelta: 0,
            ExpectedEffect: "test",
            Confidence: 0.95,
            Reason: "test");

    private static void Require(
        bool condition,
        string message)
    {
        if (!condition)
            throw new InvalidOperationException(
                message);
    }

    private sealed class AcceptanceDisplayTopologyService
        : IComputerDisplayTopologyService
    {
        public ComputerDisplayTopologyResponse GetTopology() =>
            new(
                2,
                [
                    BuildMonitor(
                        "LEFT",
                        -1920,
                        0,
                        1920,
                        1080),
                    BuildMonitor(
                        "PRIMARY",
                        0,
                        0,
                        1920,
                        1080,
                        primary: true)
                ]);

        public ComputerMonitorInfo? GetMonitorAtPoint(
            int x,
            int y) =>
            x < 0
                ? BuildMonitor(
                    "LEFT",
                    -1920,
                    0,
                    1920,
                    1080)
                : BuildMonitor(
                    "PRIMARY",
                    0,
                    0,
                    1920,
                    1080,
                    primary: true);

        private static ComputerMonitorInfo BuildMonitor(
            string name,
            int left,
            int top,
            int width,
            int height,
            bool primary = false) =>
            new(
                name,
                primary,
                left,
                top,
                width,
                height,
                left,
                top,
                width,
                height,
                96,
                96,
                1,
                1);
    }

    private sealed class AcceptanceComputerUseService
        : IComputerUseService
    {
        public const string WindowId = "0xACCE";

        public ComputerScreenInfo GetScreenInfo() =>
            new(
                1920,
                1080,
                -1920,
                0,
                3840,
                1080,
                2);

        public ComputerWindowListResponse GetWindows(
            int limit = 30) =>
            new(
                1,
                [
                    new ComputerWindowInfo(
                        WindowId,
                        "Acceptance Window",
                        "acceptance",
                        1,
                        true,
                        300,
                        200,
                        800,
                        600)
                ]);

        public ComputerUseStatusResponse GetStatus() =>
            throw Unsupported();
        public ComputerCursorPosition GetCursorPosition() =>
            throw Unsupported();
        public ComputerWindowInfo? GetActiveWindow() =>
            GetWindows().Windows[0];
        public ComputerWindowInfo? GetWindowAtPoint(int x, int y) =>
            GetWindows().Windows[0];
        public ComputerActionResponse FocusWindow(string windowId) =>
            throw Unsupported();
        public ComputerActionResponse FocusWindowByQuery(string query) =>
            throw Unsupported();
        public ComputerActionResponse MinimizeWindow(string windowId) =>
            throw Unsupported();
        public ComputerActionResponse MaximizeWindow(string windowId) =>
            throw Unsupported();
        public ComputerActionResponse RestoreWindow(string windowId) =>
            throw Unsupported();
        public ComputerActionResponse MoveCursor(int x, int y) =>
            throw Unsupported();
        public ComputerActionResponse SmoothMoveCursor(int x, int y, int durationMs = 320) =>
            throw Unsupported();
        public ComputerActionResponse ClickLeft(string windowId, int x, int y) =>
            throw Unsupported();
        public ComputerActionResponse ClickRight(string windowId, int x, int y) =>
            throw Unsupported();
        public ComputerActionResponse DoubleClickLeft(string windowId, int x, int y) =>
            throw Unsupported();
        public ComputerActionResponse Scroll(string windowId, int x, int y, int delta) =>
            throw Unsupported();
        public ComputerActionResponse DragLeft(string windowId, int startX, int startY, int endX, int endY, int durationMs = 500) =>
            throw Unsupported();
        public ComputerActionResponse TypeNotepadText(string windowId, string text) =>
            throw Unsupported();
        public ComputerActionResponse TypeText(string windowId, string text) =>
            throw Unsupported();
        public ComputerActionResponse PressKey(string windowId, string key) =>
            throw Unsupported();
        public ComputerActionResponse PressHotkey(string windowId, IReadOnlyList<string> keys) =>
            throw Unsupported();
        public ComputerActionResponse OpenDefaultBrowser(string? url = null) =>
            throw Unsupported();

        private static NotSupportedException Unsupported() =>
            new("Không dùng hành động Windows thật trong acceptance suite.");
    }
}
