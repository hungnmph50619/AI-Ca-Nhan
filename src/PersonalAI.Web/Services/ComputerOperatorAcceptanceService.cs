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
            "canonical interaction space quy về desktop ảo 0..1",
            CheckCanonicalInteractionSpace);

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
            "safe click trên màn hình có tọa độ âm",
            CheckSafeTargetOnNegativeMonitor);

        RunCheck(
            checks,
            "bộ nhớ recovery chặn lặp chiến lược thất bại",
            CheckRecoveryMemory);

        RunCheck(
            checks,
            "recovery cho phép chiến lược thay thế sau thất bại",
            CheckRecoveryAllowsAlternativeStrategy);

        RunCheck(
            checks,
            "loop guard phát hiện trạng thái đứng yên",
            CheckLoopStagnation);

        RunCheck(
            checks,
            "loop guard phát hiện dao động A-B",
            CheckLoopOscillation);

        RunCheck(
            checks,
            "loop guard buộc đổi chiến lược khi lặp cùng action",
            CheckRepeatedStrategyRequiresChange);

        RunCheck(
            checks,
            "DPI 125/150 chỉ là metadata, không scale tọa độ lần hai",
            CheckDpiMetadataWithoutDoubleScaling);

        RunCheck(
            checks,
            "target di chuyển tạo điểm click an toàn mới",
            CheckMovingTargetRecomputesSafePoint);

        RunCheck(
            checks,
            "bounding box không hợp lệ bị từ chối",
            CheckInvalidBoundingBoxRejected);

        RunCheck(
            checks,
            "local fast observer phát hiện thay đổi cục bộ không cần AI",
            CheckLocalFastObserverSignals);

        RunCheck(
            checks,
            "pause resume stop giữ đúng trạng thái và cancellation",
            CheckExecutionPauseResumeStop);

        RunCheck(
            checks,
            "mở ứng dụng bất kỳ chỉ ủy quyền thành goal tổng quát",
            CheckGenericAppLaunchDelegation);

        RunCheck(
            checks,
            "mở ứng dụng không giả định taskbar hay executable",
            CheckGenericAppLaunchDoesNotAssumeTaskbar);

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

    private static void CheckCanonicalInteractionSpace()
    {
        var transform = new ComputerCoordinateTransformService(
            new AcceptanceComputerUseService(),
            new AcceptanceDisplayTopologyService());

        var frame = new DesktopScreenshotFrame(
            Array.Empty<byte>(),
            -1920,
            0,
            3840,
            1080,
            DateTimeOffset.UtcNow);

        var canonical = transform.ToCanonicalPoint(
            new(
                ComputerCoordinateSpaces.ImagePixel,
                100,
                50,
                0,
                0,
                string.Empty),
            frame);

        Require(
            canonical.PhysicalX == -1820 &&
            canonical.PhysicalY == 50,
            $"Canonical làm thay đổi pixel vật lý: ({canonical.PhysicalX},{canonical.PhysicalY}).");

        Require(
            Math.Abs(canonical.X - (100.0 / 3839.0)) < 0.0001 &&
            Math.Abs(canonical.Y - (50.0 / 1079.0)) < 0.0001,
            $"Sai canonical desktop: ({canonical.X:0.000000},{canonical.Y:0.000000}).");

        Require(
            canonical.MonitorDevice == "LEFT" &&
            Math.Abs(canonical.MonitorX - (100.0 / 1919.0)) < 0.0001,
            $"Sai canonical monitor: {canonical.MonitorDevice} x={canonical.MonitorX:0.000000}.");

        Require(
            canonical.X is >= 0 and <= 1 &&
            canonical.Y is >= 0 and <= 1 &&
            canonical.MonitorX is >= 0 and <= 1 &&
            canonical.MonitorY is >= 0 and <= 1,
            "Canonical coordinate phải luôn nằm trong khoảng 0..1.");
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

    private static void CheckSafeTargetOnNegativeMonitor()
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
            boxLeft: 120,
            boxTop: 160,
            boxWidth: 140,
            boxHeight: 80);

        var target = targeting.Resolve(
            decision,
            frame);

        Require(
            target.Point.DesktopX < 0,
            "Safe targeting trên màn hình trái không giữ được tọa độ desktop âm.");

        var imageX = target.Point.DesktopX - frame.Left;
        var imageY = target.Point.DesktopY - frame.Top;

        Require(
            imageX > decision.BoxLeft &&
            imageX < decision.BoxLeft + decision.BoxWidth &&
            imageY > decision.BoxTop &&
            imageY < decision.BoxTop + decision.BoxHeight,
            "Safe targeting trên màn hình âm tạo điểm click ngoài bounding box.");
    }

    private static void CheckRecoveryMemory()
    {
        var recovery = new ComputerOperatorRecoverySession();

        var count = recovery.RecordFailure(
            "click-left:image-pixel:100,100",
            "click-left",
            ComputerOperatorFailureKinds.VerificationFailed,
            "Trạng thái mục tiêu chưa xuất hiện",
            "Click không tạo trạng thái mong đợi.",
            "Trạng thái mục tiêu xuất hiện",
            0.92);

        Require(
            count == 1,
            "Bộ nhớ recovery đếm lần thất bại đầu tiên sai.");

        var avoid = recovery.ShouldAvoidRepeatedStrategy(
            "click-left:image-pixel:100,100",
            "Trạng thái mục tiêu chưa xuất hiện",
            out var reason);

        Require(
            avoid &&
            !string.IsNullOrWhiteSpace(reason),
            "Recovery không chặn chiến lược vừa thất bại trong trạng thái tương đương.");
    }

    private static void CheckRecoveryAllowsAlternativeStrategy()
    {
        var recovery = new ComputerOperatorRecoverySession();

        _ = recovery.RecordFailure(
            "click-left:image-pixel:100,100",
            "click-left",
            ComputerOperatorFailureKinds.VerificationFailed,
            "Giao diện chưa đổi",
            "Không đạt kết quả mong đợi.",
            "Trạng thái mục tiêu xuất hiện",
            0.9);

        var shouldAvoidDifferent = recovery.ShouldAvoidRepeatedStrategy(
            "press-hotkey:meta+k",
            "Giao diện chưa đổi",
            out _);

        Require(
            !shouldAvoidDifferent,
            "Recovery chặn cả chiến lược thay thế dù chỉ chiến lược cũ thất bại.");
    }

    private static void CheckLoopStagnation()
    {
        var loop = new ComputerOperatorLoopGuardSession();
        ComputerOperatorLoopAssessment result = new(
            false,
            false,
            string.Empty,
            string.Empty,
            0);

        for (var index = 0; index < 4; index++)
            result = loop.Observe(
                "Cửa sổ không thay đổi",
                $"strategy-{index}");

        Require(
            result.Detected &&
            result.Kind == "stagnation",
            "Không phát hiện chuỗi trạng thái đứng yên 4 vòng.");
    }

    private static void CheckLoopOscillation()
    {
        var loop = new ComputerOperatorLoopGuardSession();

        _ = loop.Observe("Trạng thái A", "strategy-a");
        _ = loop.Observe("Trạng thái B", "strategy-b");
        _ = loop.Observe("Trạng thái A", "strategy-a");
        var result = loop.Observe("Trạng thái B", "strategy-b");

        Require(
            result.Detected &&
            result.Kind == "oscillation",
            "Không phát hiện dao động A-B-A-B.");

        loop.MarkProgress();
        var afterProgress = loop.Observe(
            "Trạng thái C",
            "strategy-c");

        Require(
            !afterProgress.Detected,
            "Loop guard không reset sau khi xác nhận có tiến triển.");
    }

    private static void CheckRepeatedStrategyRequiresChange()
    {
        var loop = new ComputerOperatorLoopGuardSession();

        _ = loop.Observe(
            "Cùng một trạng thái",
            "click-left:image-pixel:100,100");
        _ = loop.Observe(
            "Cùng một trạng thái",
            "click-left:image-pixel:100,100");
        var result = loop.Observe(
            "Cùng một trạng thái",
            "click-left:image-pixel:100,100");

        Require(
            result.Detected &&
            result.Kind == "repeated-strategy" &&
            result.RequiresStrategyChange,
            "Loop guard không bắt buộc đổi chiến lược sau khi lặp cùng action/target.");
    }

    private static void CheckDpiMetadataWithoutDoubleScaling()
    {
        var computer = new AcceptanceComputerUseService();
        var displays = new AcceptanceScaledDisplayTopologyService();
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

        var leftPoint = transform.ToDesktopPoint(
            new(
                ComputerCoordinateSpaces.ImagePixel,
                100,
                100,
                0,
                0,
                string.Empty),
            frame);

        Require(
            leftPoint.DesktopX == -1820 &&
            leftPoint.DesktopY == 100,
            "DPI 125% đã làm scale lại tọa độ pixel trên màn hình trái.");

        Require(
            leftPoint.DpiX == 120 &&
            Math.Abs(leftPoint.ScaleX - 1.25) < 0.001,
            "Không giữ đúng metadata DPI 125%.");

        var primaryPoint = transform.ToDesktopPoint(
            new(
                ComputerCoordinateSpaces.ImagePixel,
                2500,
                200,
                0,
                0,
                string.Empty),
            frame);

        Require(
            primaryPoint.DesktopX == 580 &&
            primaryPoint.DesktopY == 200,
            "DPI 150% đã làm scale lại tọa độ pixel trên màn hình chính.");

        Require(
            primaryPoint.DpiX == 144 &&
            Math.Abs(primaryPoint.ScaleX - 1.5) < 0.001,
            "Không giữ đúng metadata DPI 150%.");
    }

    private static void CheckMovingTargetRecomputesSafePoint()
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

        var first = BuildClickDecision(
            ComputerCoordinateSpaces.ImagePixel,
            boxLeft: 2100,
            boxTop: 250,
            boxWidth: 120,
            boxHeight: 70);

        var moved = BuildClickDecision(
            ComputerCoordinateSpaces.ImagePixel,
            boxLeft: 2350,
            boxTop: 410,
            boxWidth: 120,
            boxHeight: 70);

        var firstTarget = targeting.Resolve(
            first,
            frame);
        var movedTarget = targeting.Resolve(
            moved,
            frame);

        Require(
            firstTarget.Point.DesktopX != movedTarget.Point.DesktopX ||
            firstTarget.Point.DesktopY != movedTarget.Point.DesktopY,
            "Target đã di chuyển nhưng safe targeting vẫn tái sử dụng điểm click cũ.");

        var movedImageX = movedTarget.Point.DesktopX - frame.Left;
        var movedImageY = movedTarget.Point.DesktopY - frame.Top;

        Require(
            movedImageX > moved.BoxLeft &&
            movedImageX < moved.BoxLeft + moved.BoxWidth &&
            movedImageY > moved.BoxTop &&
            movedImageY < moved.BoxTop + moved.BoxHeight,
            "Điểm click mới không nằm trong bounding box mới của target.");
    }

    private static void CheckInvalidBoundingBoxRejected()
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

        var invalid = BuildClickDecision(
            ComputerCoordinateSpaces.ImagePixel,
            boxLeft: 100,
            boxTop: 100,
            boxWidth: 0,
            boxHeight: 0);

        var rejected = false;
        try
        {
            _ = targeting.Resolve(
                invalid,
                frame);
        }
        catch (ToolExecutionInputException)
        {
            rejected = true;
        }

        Require(
            rejected,
            "Safe targeting vẫn chấp nhận click khi bounding box không hợp lệ.");
    }

    private static void CheckLocalFastObserverSignals()
    {
        var observer = new DesktopLocalFastObserver(
            new AcceptanceComputerUseService(),
            new AcceptanceDisplayTopologyService());

        var before = new DesktopFastObserverSample(
            DateTimeOffset.UtcNow,
            "window",
            AcceptanceComputerUseService.WindowId,
            AcceptanceComputerUseService.WindowId,
            300,
            200,
            800,
            600,
            400,
            300,
            "PRIMARY",
            96,
            96,
            false);

        var after = before with
        {
            CapturedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(100),
            ActiveWindowId = "0xBEEF",
            WindowLeft = 320,
            CursorX = 460,
            DpiX = 120
        };

        var targetBefore = new DesktopSceneElement(
            "target-before",
            "button",
            "Mục tiêu",
            string.Empty,
            100,
            100,
            120,
            60,
            0.95,
            Array.Empty<string>());

        var targetAfter = targetBefore with
        {
            Id = "target-after",
            BoxLeft = 140,
            BoxTop = 130
        };

        var frameDifference = new DesktopFrameDifference(
            true,
            0.12,
            120,
            1000,
            90,
            90,
            220,
            140,
            31,
            "acceptance");

        var result = observer.Analyze(
            before,
            after,
            frameDifference,
            targetBefore,
            targetAfter);

        Require(
            result.ScreenChanged &&
            result.ForegroundWindowChanged &&
            result.WindowBoundsChanged &&
            result.CursorMoved &&
            result.DpiChanged &&
            result.TargetMoved &&
            !result.TargetMissing &&
            !result.TargetLikelyOccluded,
            $"Fast observer bỏ sót tín hiệu: {result.Summary}");
    }

    private static void CheckExecutionPauseResumeStop()
    {
        using var execution = new ComputerOperatorExecutionControl();

        var token = execution.Begin(
            "acceptance-test");

        Require(
            execution.Running &&
            !execution.Paused &&
            execution.Pausable,
            "Execution control không vào trạng thái running đúng.");

        Require(
            execution.Pause() &&
            execution.Paused,
            "Pause không chuyển task sang paused.");

        Require(
            execution.Resume() &&
            !execution.Paused &&
            execution.Running,
            "Resume không khôi phục task đang chạy.");

        Require(
            execution.Stop(),
            "Stop không dừng task đang chạy.");

        Require(
            token.IsCancellationRequested &&
            !execution.Running &&
            !execution.Paused &&
            !execution.Pausable,
            "Stop không hủy token hoặc không xóa trạng thái điều khiển.");
    }

    private static void CheckGenericAppLaunchDelegation()
    {
        var fake = new AcceptanceOperatorTaskService();
        var tool = new ComputerGenericAppLaunchTool(
            fake);

        using var document = System.Text.Json.JsonDocument.Parse(
            """{"application":"Ứng dụng thử nghiệm Ω"}""");

        var result = tool.ExecuteAsync(
                document.RootElement)
            .GetAwaiter()
            .GetResult();

        Require(
            fake.CallCount == 1,
            "computer.app.launch không ủy quyền đúng một lần cho Computer Operator.");

        Require(
            fake.LastGoal.Contains(
                "Ứng dụng thử nghiệm Ω",
                StringComparison.Ordinal) &&
            fake.LastGoal.Contains(
                "tự quan sát",
                StringComparison.OrdinalIgnoreCase),
            "computer.app.launch không chuyển tên ứng dụng bất kỳ thành goal quan sát tổng quát.");

        Require(
            !fake.LastGoal.Contains(
                ".exe",
                StringComparison.OrdinalIgnoreCase),
            "Goal mở ứng dụng chứa đường dẫn/executable hard-code.");

        Require(
            result.ValueKind == System.Text.Json.JsonValueKind.Object,
            "computer.app.launch không trả kết quả dạng object.");
    }

    private static void CheckGenericAppLaunchDoesNotAssumeTaskbar()
    {
        var fake = new AcceptanceOperatorTaskService();
        var tool = new ComputerGenericAppLaunchTool(
            fake);

        using var document = System.Text.Json.JsonDocument.Parse(
            """{"application":"Ứng dụng bất kỳ Δ"}""");

        _ = tool.ExecuteAsync(
                document.RootElement)
            .GetAwaiter()
            .GetResult();

        Require(
            fake.LastGoal.Contains(
                "không giả định ứng dụng được ghim trên taskbar",
                StringComparison.OrdinalIgnoreCase),
            "Goal mở ứng dụng vẫn giả định ứng dụng có trên taskbar.");

        Require(
            fake.LastGoal.Contains(
                "Nếu ứng dụng đã có cửa sổ",
                StringComparison.OrdinalIgnoreCase),
            "Goal mở ứng dụng không xét trường hợp ứng dụng đã mở sẵn.");

        Require(
            !fake.LastGoal.Contains(
                ".exe",
                StringComparison.OrdinalIgnoreCase),
            "Goal mở ứng dụng chứa executable hard-code.");
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

    private sealed class AcceptanceScaledDisplayTopologyService
        : IComputerDisplayTopologyService
    {
        public ComputerDisplayTopologyResponse GetTopology() =>
            new(
                2,
                [
                    BuildMonitor(
                        "LEFT-125",
                        -1920,
                        0,
                        1920,
                        1080,
                        120,
                        1.25),
                    BuildMonitor(
                        "PRIMARY-150",
                        0,
                        0,
                        1920,
                        1080,
                        144,
                        1.5,
                        primary: true)
                ]);

        public ComputerMonitorInfo? GetMonitorAtPoint(
            int x,
            int y) =>
            x < 0
                ? BuildMonitor(
                    "LEFT-125",
                    -1920,
                    0,
                    1920,
                    1080,
                    120,
                    1.25)
                : BuildMonitor(
                    "PRIMARY-150",
                    0,
                    0,
                    1920,
                    1080,
                    144,
                    1.5,
                    primary: true);

        private static ComputerMonitorInfo BuildMonitor(
            string name,
            int left,
            int top,
            int width,
            int height,
            uint dpi,
            double scale,
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
                dpi,
                dpi,
                scale,
                scale);
    }

    private sealed class AcceptanceOperatorTaskService
        : IComputerOperatorTaskService
    {
        public int CallCount { get; private set; }
        public string LastGoal { get; private set; } = string.Empty;

        public Task<ComputerOperatorTaskResult> RunAsync(
            string goal,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastGoal = goal;

            return Task.FromResult(
                new ComputerOperatorTaskResult(
                    goal,
                    true,
                    "acceptance",
                    Array.Empty<ComputerOperatorTaskStep>(),
                    "acceptance",
                    "acceptance"));
        }
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
