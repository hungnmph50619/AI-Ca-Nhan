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
            "verification router xác minh local khi tín hiệu cấu trúc đủ chắc",
            CheckVerificationRouterLocalPass);

        RunCheck(
            checks,
            "verification router fallback Gemini khi cần hiểu semantic",
            CheckVerificationRouterGeminiFallback);

        RunCheck(
            checks,
            "ROI Vision ưu tiên target box khi cùng không gian ảnh",
            CheckRoiVisionTargetPriority);

        RunCheck(
            checks,
            "ROI Vision fallback vùng thay đổi khi target box không dùng được",
            CheckRoiVisionChangedRegionFallback);

        RunCheck(
            checks,
            "adaptive Gemini bỏ qua verify khi action không làm UI thay đổi",
            CheckAdaptiveGeminiSkipsNoChange);

        RunCheck(
            checks,
            "adaptive Gemini vẫn gọi AI khi context desktop đổi mạnh",
            CheckAdaptiveGeminiCallsOnContextChange);

        RunCheck(
            checks,
            "dynamic target tracker remap bbox khi cửa sổ di chuyển",
            CheckDynamicTargetTrackerMovesWithWindow);

        RunCheck(
            checks,
            "dynamic target tracker chặn click khi window identity đổi",
            CheckDynamicTargetTrackerRejectsDifferentWindow);

        RunCheck(
            checks,
            "coordinate engine v2 map monitor-normalized trên màn hình tọa độ âm",
            CheckMonitorNormalizedCoordinates);

        RunCheck(
            checks,
            "coordinate engine v2 giữ đúng origin của ROI",
            CheckRoiOriginMapsToPhysicalDesktop);

        RunCheck(
            checks,
            "action state machine đi đúng happy path",
            CheckActionStateMachineHappyPath);

        RunCheck(
            checks,
            "action state machine chặn execute trước target",
            CheckActionStateMachineRejectsInvalidTransition);

        RunCheck(
            checks,
            "confidence engine cho execute khi scene target action đều đủ chắc",
            CheckConfidenceEngineAllowsExecution);

        RunCheck(
            checks,
            "confidence engine buộc reobserve khi target yếu",
            CheckConfidenceEngineRejectsWeakTarget);

        RunCheck(
            checks,
            "failure recovery ưu tiên retarget khi target biến mất",
            CheckFailureRecoveryRetargetsMissingTarget);

        RunCheck(
            checks,
            "failure recovery không retry y hệt khi lỗi lặp nhiều lần",
            CheckFailureRecoveryEscalatesRepeatedFailure);

        RunCheck(
            checks,
            "anti-loop phát hiện cùng scene action outcome lặp lại",
            CheckRepeatedOutcomeFingerprint);

        RunCheck(
            checks,
            "anti-loop reset outcome fingerprint sau khi có progress",
            CheckOutcomeFingerprintResetsAfterProgress);

        RunCheck(
            checks,
            "execution agent chỉ nhận channel computer",
            CheckExecutionAgentChannelBoundary);

        RunCheck(
            checks,
            "execution agent registry bọc Computer Operator đúng contract",
            CheckExecutionAgentRegistry);

        RunCheck(
            checks,
            "Microsoft Agent Framework adapter giữ approval cho side effect",
            CheckMicrosoftAgentFrameworkApprovalBoundary);

        RunCheck(
            checks,
            "Microsoft Agent Framework adapter báo đúng package và execution agents",
            CheckMicrosoftAgentFrameworkAdapterStatus);

        RunCheck(
            checks,
            "tool capability registry phân loại Computer Operator đúng capability và risk",
            CheckToolCapabilityRegistryComputerOperator);

        RunCheck(
            checks,
            "tool capability registry giữ tool read-only ở low risk",
            CheckToolCapabilityRegistryReadOnlyTool);

        RunCheck(
            checks,
            "capability router chỉ chọn tool đúng channel",
            CheckCapabilityRouterKeepsChannelBoundary);

        RunCheck(
            checks,
            "capability router chặn high risk mặc định",
            CheckCapabilityRouterBlocksHighRiskByDefault);

        RunCheck(
            checks,
            "browser execution agent giữ đúng browser channel",
            CheckBrowserExecutionAgentChannelBoundary);

        RunCheck(
            checks,
            "browser execution agent bọc backend đúng contract",
            CheckBrowserExecutionAgentBackendContract);

        RunCheck(
            checks,
            "coding execution agent chỉ nhận explicit coding command",
            CheckCodingExecutionAgentExplicitCommandBoundary);

        RunCheck(
            checks,
            "coding execution agent bọc backend đúng contract",
            CheckCodingExecutionAgentBackendContract);

        RunCheck(
            checks,
            "OpenHands backend chỉ nhận explicit request khi adapter bật",
            CheckOpenHandsExplicitRequestBoundary);

        RunCheck(
            checks,
            "OpenHands backend khởi tạo conversation ở chế độ cần xác nhận",
            CheckOpenHandsConversationContract);

        RunCheck(
            checks,
            "coding verification gate bắt buộc restore build test diff",
            CheckCodingVerificationGatePassesAllRequiredSteps);

        RunCheck(
            checks,
            "coding verification gate dừng khi build fail",
            CheckCodingVerificationGateStopsOnBuildFailure);

        RunCheck(
            checks,
            "integration architecture giữ Computer Operator là core sở hữu",
            CheckIntegrationArchitectureOwnsComputerOperator);

        RunCheck(
            checks,
            "integration architecture giữ external engines thay thế được",
            CheckIntegrationArchitectureKeepsExternalAdaptersReplaceable);

        RunCheck(
            checks,
            "execution gateway route đúng agent theo channel",
            CheckExecutionGatewayRoutesByChannel);

        RunCheck(
            checks,
            "execution gateway chặn side effect khi chưa xác nhận",
            CheckExecutionGatewayRequiresConfirmation);

        RunCheck(
            checks,
            "universal router chọn browser khi có URL rõ ràng",
            CheckUniversalRouterSelectsBrowserForUrl);

        RunCheck(
            checks,
            "universal router chọn coding cho task code rõ ràng",
            CheckUniversalRouterSelectsCodingForCodeTask);

        RunCheck(
            checks,
            "universal router không tự đoán goal mơ hồ",
            CheckUniversalRouterRejectsAmbiguousGoal);

        RunCheck(
            checks,
            "cost latency router ưu tiên route rẻ nhanh khi confidence ngang nhau",
            CheckUniversalRouterPrefersLowerCostRoute);

        RunCheck(
            checks,
            "universal router nhận URL có ký tự s sau scheme",
            CheckUniversalRouterUrlRegexRegression);

        RunCheck(
            checks,
            "capability first router phát hiện direct tool an toàn",
            CheckUniversalRouterDetectsDirectToolOpportunity);

        RunCheck(
            checks,
            "capability first router không coi Computer Operator là direct tool",
            CheckUniversalRouterExcludesComputerOperatorFromDirectTools);

        RunCheck(
            checks,
            "tool argument planner chấp nhận arguments đúng schema",
            CheckToolArgumentPlannerAcceptsValidArguments);

        RunCheck(
            checks,
            "tool argument planner chặn arguments sai schema",
            CheckToolArgumentPlannerRejectsInvalidArguments);

        RunCheck(
            checks,
            "direct tool path tạo proposal nhưng chưa execute",
            CheckUniversalDirectToolPathPreparesWithoutExecution);

        RunCheck(
            checks,
            "direct tool path giữ execution agent làm fallback khi không có tool",
            CheckUniversalDirectToolPathFallsBackWithoutExecuting);

        RunCheck(
            checks,
            "fallback policy không lách permission denial sang agent",
            CheckUniversalFallbackPolicyBlocksDeniedBypass);

        RunCheck(
            checks,
            "fallback policy chỉ cho agent fallback sau lỗi kỹ thuật",
            CheckUniversalFallbackPolicyAllowsTechnicalFallback);

        RunCheck(
            checks,
            "outcome verifier không coi tool success là goal success",
            CheckUniversalOutcomeRequiresEvidence);

        RunCheck(
            checks,
            "outcome verifier buộc replan khi evidence xác nhận goal chưa đạt",
            CheckUniversalOutcomeFailedEvidenceReplans);

        RunCheck(
            checks,
            "outcome verifier chỉ complete khi evidence đủ mạnh và pass",
            CheckUniversalOutcomeVerifiedEvidenceCompletes);

        RunCheck(
            checks,
            "verification evidence router ưu tiên verifier riêng của tool",
            CheckVerificationEvidenceRouterPrefersToolVerifier);

        RunCheck(
            checks,
            "verification evidence router dùng browser readback trước semantic AI",
            CheckVerificationEvidenceRouterUsesBrowserReadback);

        RunCheck(
            checks,
            "verification evidence router dùng observe verify cho computer",
            CheckVerificationEvidenceRouterUsesComputerVerifier);

        RunCheck(
            checks,
            "evidence adapter chuyển browser verified thành universal evidence",
            CheckVerificationEvidenceAdapterBrowser);

        RunCheck(
            checks,
            "evidence adapter chuyển coding gate fail thành evidence fail",
            CheckVerificationEvidenceAdapterCodingFailure);

        RunCheck(
            checks,
            "evidence adapter giữ desktop local failure",
            CheckVerificationEvidenceAdapterDesktopFailure);

        RunCheck(
            checks,
            "evidence adapter không bịa evidence khi desktop cần Gemini",
            CheckVerificationEvidenceAdapterDesktopSemanticFallback);

        RunCheck(
            checks,
            "evidence collector tự browser readback sau execution",
            CheckVerificationEvidenceCollectorBrowserReadback);

        RunCheck(
            checks,
            "evidence collector không đoán coding context khi thiếu path",
            CheckVerificationEvidenceCollectorCodingNeedsContext);

        RunCheck(
            checks,
            "evidence collector chạy coding verification gate khi đủ context",
            CheckVerificationEvidenceCollectorCodingGate);

        RunCheck(
            checks,
            "evidence collector không bịa desktop evidence khi cần semantic vision",
            CheckVerificationEvidenceCollectorDesktopSemanticFallback);

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

    private static void CheckVerificationRouterLocalPass()
    {
        var router = new DesktopVerificationRouter();
        var decision = BuildClickDecision(
            ComputerCoordinateSpaces.ImagePixel) with
        {
            Action = "maximize",
            ExpectedEffect = "Cửa sổ được phóng to"
        };

        var observation = new DesktopFastObservation(
            ScreenChanged: true,
            ChangeRatio: 0.08,
            ForegroundWindowChanged: false,
            WindowBoundsChanged: true,
            CursorMoved: false,
            MonitorChanged: false,
            DpiChanged: false,
            TargetMoved: false,
            TargetMissing: false,
            TargetLikelyOccluded: false,
            Summary: "acceptance");

        var route = router.Route(
            decision,
            observation,
            new DesktopFrameDifference(
                true, 0.08, 80, 1000,
                0, 0, 200, 100, 20, "acceptance"));

        Require(
            route.Route == DesktopVerificationRoute.LocalVerified &&
            route.Confidence >= 0.9,
            "Verification Router không xác minh local cho thay đổi cửa sổ chắc chắn.");
    }

    private static void CheckVerificationRouterGeminiFallback()
    {
        var router = new DesktopVerificationRouter();
        var decision = BuildClickDecision(
            ComputerCoordinateSpaces.ImagePixel) with
        {
            Action = "click-left",
            ExpectedEffect = "Nội dung semantic cụ thể xuất hiện"
        };

        var observation = new DesktopFastObservation(
            ScreenChanged: true,
            ChangeRatio: 0.15,
            ForegroundWindowChanged: false,
            WindowBoundsChanged: false,
            CursorMoved: true,
            MonitorChanged: false,
            DpiChanged: false,
            TargetMoved: false,
            TargetMissing: false,
            TargetLikelyOccluded: false,
            Summary: "acceptance");

        var route = router.Route(
            decision,
            observation,
            new DesktopFrameDifference(
                true, 0.15, 150, 1000,
                20, 20, 300, 200, 30, "acceptance"));

        Require(
            route.Route == DesktopVerificationRoute.GeminiRequired,
            "Verification Router đã tự xác minh click semantic chỉ từ frame difference.");
    }

    private static void CheckRoiVisionTargetPriority()
    {
        var service = new DesktopRoiVisionService();
        using var jpeg = BuildAcceptanceJpeg(800, 600);
        var bytes = jpeg.ToArray();

        var frame = new DesktopScreenshotFrame(
            bytes.ToArray(),
            0,
            0,
            800,
            600,
            DateTimeOffset.UtcNow);

        var decision = BuildClickDecision(
            ComputerCoordinateSpaces.ImagePixel,
            boxLeft: 300,
            boxTop: 220,
            boxWidth: 100,
            boxHeight: 60);

        var selection = service.SelectVerificationFrame(
            decision,
            frame,
            frame,
            new DesktopFrameDifference(
                true, 0.2, 200, 1000,
                10, 10, 100, 100, 30, "acceptance"));

        try
        {
            Require(
                selection.Source == "target-roi" &&
                selection.Frame.Width < frame.Width &&
                selection.Frame.Height < frame.Height &&
                selection.Frame.Left <= decision.BoxLeft &&
                selection.Frame.Top <= decision.BoxTop,
                "ROI Vision không ưu tiên/cắt đúng vùng target.");
        }
        finally
        {
            selection.Clear();
            frame.Clear();
        }
    }

    private static void CheckRoiVisionChangedRegionFallback()
    {
        var service = new DesktopRoiVisionService();
        using var jpeg = BuildAcceptanceJpeg(800, 600);
        var bytes = jpeg.ToArray();

        var planning = new DesktopScreenshotFrame(
            bytes.ToArray(),
            -100,
            0,
            800,
            600,
            DateTimeOffset.UtcNow);

        var current = new DesktopScreenshotFrame(
            bytes.ToArray(),
            0,
            0,
            800,
            600,
            DateTimeOffset.UtcNow,
            CaptureScope: "window",
            WindowId: AcceptanceComputerUseService.WindowId);

        var decision = BuildClickDecision(
            ComputerCoordinateSpaces.ImagePixel,
            boxLeft: 300,
            boxTop: 220,
            boxWidth: 100,
            boxHeight: 60);

        var selection = service.SelectVerificationFrame(
            decision,
            planning,
            current,
            new DesktopFrameDifference(
                true, 0.12, 120, 1000,
                250, 180, 120, 90, 25, "acceptance"));

        try
        {
            Require(
                selection.Source == "changed-region" &&
                selection.Frame.Width < current.Width &&
                selection.Frame.Height < current.Height,
                "ROI Vision không fallback về changed-region khi target box không cùng không gian.");
        }
        finally
        {
            selection.Clear();
            planning.Clear();
            current.Clear();
        }
    }

    private static MemoryStream BuildAcceptanceJpeg(
        int width,
        int height)
    {
        using var bitmap = new System.Drawing.Bitmap(
            width,
            height,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.White);
        graphics.FillRectangle(
            System.Drawing.Brushes.Black,
            width / 3,
            height / 3,
            width / 4,
            height / 4);

        var stream = new MemoryStream();
        bitmap.Save(
            stream,
            System.Drawing.Imaging.ImageFormat.Jpeg);
        stream.Position = 0;
        return stream;
    }

    private static void CheckAdaptiveGeminiSkipsNoChange()
    {
        var policy = new AdaptiveGeminiCallPolicy();
        var decision = BuildClickDecision(
            ComputerCoordinateSpaces.ImagePixel) with
        {
            Action = "click-left",
            ExpectedEffect = "UI thay đổi"
        };

        var observation = new DesktopFastObservation(
            ScreenChanged: false,
            ChangeRatio: 0,
            ForegroundWindowChanged: false,
            WindowBoundsChanged: false,
            CursorMoved: true,
            MonitorChanged: false,
            DpiChanged: false,
            TargetMoved: false,
            TargetMissing: false,
            TargetLikelyOccluded: false,
            Summary: "acceptance");

        var result = policy.EvaluateVerification(
            decision,
            observation,
            new DesktopFrameDifference(
                true,
                0.0005,
                1,
                2000,
                0,
                0,
                4,
                4,
                1,
                "acceptance"));

        Require(
            result.Decision == AdaptiveGeminiDecision.SkipAndFail &&
            result.Confidence >= 0.9,
            "Adaptive Gemini vẫn gọi model dù action không tạo thay đổi hình ảnh.");
    }

    private static void CheckAdaptiveGeminiCallsOnContextChange()
    {
        var policy = new AdaptiveGeminiCallPolicy();
        var decision = BuildClickDecision(
            ComputerCoordinateSpaces.ImagePixel) with
        {
            Action = "click-left",
            ExpectedEffect = "Popup mới xuất hiện"
        };

        var observation = new DesktopFastObservation(
            ScreenChanged: true,
            ChangeRatio: 0.08,
            ForegroundWindowChanged: true,
            WindowBoundsChanged: false,
            CursorMoved: true,
            MonitorChanged: false,
            DpiChanged: false,
            TargetMoved: false,
            TargetMissing: false,
            TargetLikelyOccluded: false,
            Summary: "acceptance");

        var result = policy.EvaluateVerification(
            decision,
            observation,
            new DesktopFrameDifference(
                true,
                0.08,
                80,
                1000,
                0,
                0,
                200,
                120,
                20,
                "acceptance"));

        Require(
            result.Decision == AdaptiveGeminiDecision.CallGemini,
            "Adaptive Gemini bỏ qua model dù foreground/context vừa thay đổi.");
    }

    private static void CheckDynamicTargetTrackerMovesWithWindow()
    {
        var tracker = new DesktopDynamicTargetTracker();
        var frame = new DesktopScreenshotFrame(
            Array.Empty<byte>(),
            0,
            0,
            1920,
            1080,
            DateTimeOffset.UtcNow);

        var plannedWindow = new ComputerWindowInfo(
            "0xAAAA",
            "Planned",
            "acceptance",
            1,
            true,
            100,
            100,
            800,
            600);

        var currentWindow = plannedWindow with
        {
            Left = 300,
            Top = 220
        };

        var decision = BuildClickDecision(
            ComputerCoordinateSpaces.ImagePixel,
            boxLeft: 300,
            boxTop: 250,
            boxWidth: 120,
            boxHeight: 60);

        var result = tracker.Track(
            decision,
            frame,
            plannedWindow,
            currentWindow);

        Require(
            result.SafeToExecute &&
            result.Adjusted &&
            result.Decision.BoxLeft == 500 &&
            result.Decision.BoxTop == 370,
            $"Target không bám đúng theo cửa sổ: {result.Reason}");
    }

    private static void CheckDynamicTargetTrackerRejectsDifferentWindow()
    {
        var tracker = new DesktopDynamicTargetTracker();
        var frame = new DesktopScreenshotFrame(
            Array.Empty<byte>(),
            0,
            0,
            1920,
            1080,
            DateTimeOffset.UtcNow);

        var plannedWindow = new ComputerWindowInfo(
            "0xAAAA",
            "Planned",
            "acceptance",
            1,
            true,
            100,
            100,
            800,
            600);

        var currentWindow = plannedWindow with
        {
            WindowId = "0xBBBB"
        };

        var decision = BuildClickDecision(
            ComputerCoordinateSpaces.ImagePixel,
            boxLeft: 300,
            boxTop: 250,
            boxWidth: 120,
            boxHeight: 60);

        var result = tracker.Track(
            decision,
            frame,
            plannedWindow,
            currentWindow);

        Require(
            !result.SafeToExecute &&
            !result.Adjusted,
            "Target tracker vẫn cho click khi identity cửa sổ đã thay đổi.");
    }

    private static void CheckMonitorNormalizedCoordinates()
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

        var point = transform.ToDesktopPoint(
            new ComputerCoordinateRequest(
                ComputerCoordinateSpaces.MonitorNormalized,
                0,
                0,
                0.5,
                0.5,
                string.Empty,
                "LEFT"),
            frame);

        Require(
            point.DesktopX is >= -961 and <= -960 &&
            point.DesktopY is >= 539 and <= 540 &&
            point.MonitorDevice == "LEFT",
            $"Monitor-normalized map sai: ({point.DesktopX},{point.DesktopY}), monitor={point.MonitorDevice}.");
    }

    private static void CheckRoiOriginMapsToPhysicalDesktop()
    {
        var transform = new ComputerCoordinateTransformService(
            new AcceptanceComputerUseService(),
            new AcceptanceDisplayTopologyService());

        var roi = new DesktopScreenshotFrame(
            Array.Empty<byte>(),
            -450,
            220,
            400,
            300,
            DateTimeOffset.UtcNow,
            CaptureScope: "roi");

        var point = transform.ToDesktopPoint(
            new ComputerCoordinateRequest(
                ComputerCoordinateSpaces.ImagePixel,
                125,
                80,
                0,
                0,
                string.Empty),
            roi);

        Require(
            point.DesktopX == -325 &&
            point.DesktopY == 300,
            $"ROI origin bị mất khi map về desktop vật lý: ({point.DesktopX},{point.DesktopY}).");
    }

    private static void CheckActionStateMachineHappyPath()
    {
        var machine = new ComputerOperatorActionStateMachine();

        _ = machine.StartObservation("observe");
        _ = machine.MoveTo(
            ComputerOperatorActionState.Plan,
            "plan");
        _ = machine.MoveTo(
            ComputerOperatorActionState.Target,
            "target");
        _ = machine.MoveTo(
            ComputerOperatorActionState.Execute,
            "execute");
        _ = machine.MoveTo(
            ComputerOperatorActionState.Verify,
            "verify");
        var success = machine.MoveTo(
            ComputerOperatorActionState.Success,
            "success");

        Require(
            success.State == ComputerOperatorActionState.Success &&
            success.TransitionCount == 6,
            "Action state machine không hoàn thành đúng happy path.");
    }

    private static void CheckActionStateMachineRejectsInvalidTransition()
    {
        var machine = new ComputerOperatorActionStateMachine();

        _ = machine.StartObservation("observe");
        _ = machine.MoveTo(
            ComputerOperatorActionState.Plan,
            "plan");

        var rejected = false;
        try
        {
            _ = machine.MoveTo(
                ComputerOperatorActionState.Execute,
                "execute-too-early");
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Require(
            rejected,
            "Action state machine vẫn cho EXECUTE trước TARGET.");
    }

    private static void CheckConfidenceEngineAllowsExecution()
    {
        var engine = new ComputerOperatorConfidenceEngine();
        var decision = BuildClickDecision(
            ComputerCoordinateSpaces.ImagePixel,
            boxLeft: 300,
            boxTop: 220,
            boxWidth: 120,
            boxHeight: 60) with
        {
            Confidence = 0.94,
            TargetElementId = "save",
            TargetLabel = "Save",
            ExpectedEffect = "Dialog lưu được mở"
        };

        var scene = new[]
        {
            new DesktopSceneElement(
                "save",
                "button",
                "Save",
                string.Empty,
                300,
                220,
                120,
                60,
                0.96,
                Array.Empty<string>())
        };

        var tracking = new DesktopTargetTrackingResult(
            decision,
            false,
            true,
            0.97,
            "stable");

        var result = engine.AssessBeforeExecution(
            decision,
            scene,
            tracking);

        Require(
            result.Decision == ComputerOperatorConfidenceDecision.Execute &&
            result.SceneConfidence >= 0.9 &&
            result.TargetConfidence >= 0.9 &&
            result.ActionConfidence >= 0.9 &&
            result.OverallConfidence >= 0.8,
            $"Confidence Engine từ chối action tốt: {result.Reason}");
    }

    private static void CheckConfidenceEngineRejectsWeakTarget()
    {
        var engine = new ComputerOperatorConfidenceEngine();
        var decision = BuildClickDecision(
            ComputerCoordinateSpaces.ImagePixel,
            boxLeft: 300,
            boxTop: 220,
            boxWidth: 120,
            boxHeight: 60) with
        {
            Confidence = 0.66,
            TargetElementId = "missing-target",
            TargetLabel = "Unknown",
            ExpectedEffect = "UI thay đổi"
        };

        var tracking = new DesktopTargetTrackingResult(
            decision,
            false,
            false,
            0.0,
            "target identity lost");

        var result = engine.AssessBeforeExecution(
            decision,
            Array.Empty<DesktopSceneElement>(),
            tracking);

        Require(
            result.Decision != ComputerOperatorConfidenceDecision.Execute &&
            result.TargetConfidence < 0.5,
            $"Confidence Engine vẫn cho execute target yếu: {result.Reason}");
    }

    private static void CheckFailureRecoveryRetargetsMissingTarget()
    {
        var engine = new ComputerOperatorFailureRecoveryEngine();

        var plan = engine.Plan(
            new ComputerOperatorFailureContext(
                ComputerOperatorFailureTaxonomy.TargetNotFound,
                "click-left",
                "Target biến mất",
                TargetMissing: true));

        Require(
            plan.PrimaryAction == ComputerOperatorRecoveryAction.Retarget &&
            !plan.AllowSameStrategyRetry &&
            plan.Fallbacks.Contains(
                ComputerOperatorRecoveryAction.GeminiInspect),
            "Failure Recovery không ưu tiên retarget khi target biến mất.");
    }

    private static void CheckFailureRecoveryEscalatesRepeatedFailure()
    {
        var engine = new ComputerOperatorFailureRecoveryEngine();

        var plan = engine.Plan(
            new ComputerOperatorFailureContext(
                ComputerOperatorFailureTaxonomy.ActionNoEffect,
                "click-left",
                "Action không tạo hiệu ứng",
                RepeatedFailures: 3));

        Require(
            plan.PrimaryAction == ComputerOperatorRecoveryAction.GeminiInspect &&
            !plan.AllowSameStrategyRetry &&
            plan.Fallbacks.Contains(
                ComputerOperatorRecoveryAction.Block),
            "Failure Recovery không escalate khi cùng lỗi lặp nhiều lần.");
    }

    private static void CheckRepeatedOutcomeFingerprint()
    {
        var loop = new ComputerOperatorLoopGuardSession();

        _ = loop.ObserveOutcome(
            "Cùng trạng thái",
            "click-left:image-pixel:100,100",
            "không tạo thay đổi");

        var result = loop.ObserveOutcome(
            "Cùng trạng thái",
            "click-left:image-pixel:100,100",
            "không tạo thay đổi");

        Require(
            result.Detected &&
            result.RequiresStrategyChange &&
            result.Kind == "repeated-outcome",
            "Anti-loop không phát hiện scene + action + outcome lặp lại.");
    }

    private static void CheckOutcomeFingerprintResetsAfterProgress()
    {
        var loop = new ComputerOperatorLoopGuardSession();

        _ = loop.ObserveOutcome(
            "Cùng trạng thái",
            "click-left:image-pixel:100,100",
            "không tạo thay đổi");

        loop.MarkProgress();

        var result = loop.ObserveOutcome(
            "Cùng trạng thái",
            "click-left:image-pixel:100,100",
            "không tạo thay đổi");

        Require(
            !result.Detected,
            "Anti-loop không reset outcome fingerprint sau progress.");
    }

    private static void CheckExecutionAgentChannelBoundary()
    {
        var agent = new ComputerOperatorExecutionAgent(
            new AcceptanceOperatorTaskService());

        var accepted = agent.CanHandle(
            new ExecutionAgentRequest(
                "Mở ứng dụng thử nghiệm",
                ExecutionAgentChannels.Computer),
            out var acceptedConfidence,
            out _);

        var rejected = agent.CanHandle(
            new ExecutionAgentRequest(
                "Mở trang web",
                ExecutionAgentChannels.Browser),
            out var rejectedConfidence,
            out _);

        Require(
            accepted &&
            acceptedConfidence >= 0.9 &&
            !rejected &&
            rejectedConfidence == 0,
            "Computer Operator execution agent không giữ đúng channel boundary.");
    }

    private static void CheckExecutionAgentRegistry()
    {
        var fakeTask = new AcceptanceOperatorTaskService();
        IExecutionAgent agent =
            new ComputerOperatorExecutionAgent(fakeTask);
        var registry = new ExecutionAgentRegistry(
            new[] { agent });

        Require(
            registry.TryGet(
                ComputerOperatorExecutionAgent.AgentId,
                out var resolved) &&
            resolved is not null,
            "Execution Agent Registry không resolve được Computer Operator.");

        Require(
            registry.FindByChannel(
                ExecutionAgentChannels.Computer).Count == 1 &&
            registry.FindByChannel(
                ExecutionAgentChannels.Browser).Count == 0,
            "Execution Agent Registry route sai channel.");

        var result = resolved!.ExecuteAsync(
                new ExecutionAgentRequest(
                    "Mở ứng dụng thử nghiệm",
                    ExecutionAgentChannels.Computer))
            .GetAwaiter()
            .GetResult();

        Require(
            fakeTask.CallCount == 1 &&
            result.AgentId == ComputerOperatorExecutionAgent.AgentId &&
            result.Status == AgentExecutionStatuses.Succeeded &&
            result.Verified,
            "Execution agent không bọc đúng Computer Operator task result.");
    }

    private static void CheckMicrosoftAgentFrameworkApprovalBoundary()
    {
        IExecutionAgent executionAgent =
            new ComputerOperatorExecutionAgent(
                new AcceptanceOperatorTaskService());
        var registry = new ExecutionAgentRegistry(
            new[] { executionAgent });
        var adapter = new MicrosoftAgentFrameworkAdapter(
            registry);

        var tool = adapter.CreateApprovalRequiredTool(
            executionAgent);

        Require(
            tool is Microsoft.Extensions.AI.ApprovalRequiredAIFunction,
            "Execution agent side effect chưa được bọc ApprovalRequiredAIFunction.");
    }

    private static void CheckMicrosoftAgentFrameworkAdapterStatus()
    {
        IExecutionAgent executionAgent =
            new ComputerOperatorExecutionAgent(
                new AcceptanceOperatorTaskService());
        var registry = new ExecutionAgentRegistry(
            new[] { executionAgent });
        var adapter = new MicrosoftAgentFrameworkAdapter(
            registry);

        var status = adapter.GetStatus();

        Require(
            status.Package == "Microsoft.Agents.AI" &&
            status.ExecutionAgents == 1 &&
            status.ApprovalRequiredForSideEffects &&
            !status.DirectAutonomousExecutionEnabled &&
            !string.IsNullOrWhiteSpace(status.Version),
            "Microsoft Agent Framework adapter status không giữ đúng boundary an toàn.");
    }

    private static void CheckToolCapabilityRegistryComputerOperator()
    {
        IPersonalAiTool operatorTool =
            new ComputerOperatorTaskTool(
                new AcceptanceOperatorTaskService());

        var registry = new ToolCapabilityRegistry(
            new ToolRegistry(
                new[] { operatorTool }));

        Require(
            registry.TryGet(
                "computer.operator.run-task",
                out var capability) &&
            capability is not null,
            "Capability registry không resolve được Computer Operator tool.");

        Require(
            capability!.Capabilities.Contains(
                "computer",
                StringComparer.OrdinalIgnoreCase) &&
            capability.Capabilities.Contains(
                "side-effect",
                StringComparer.OrdinalIgnoreCase) &&
            capability.RiskLevel == ToolRiskLevels.High &&
            capability.RequiresConfirmation &&
            capability.SupportsVerification,
            "Computer Operator tool bị phân loại sai capability/risk/verification.");
    }

    private static void CheckToolCapabilityRegistryReadOnlyTool()
    {
        IPersonalAiTool screenTool =
            new ComputerScreenInfoTool(
                new AcceptanceComputerUseService());

        var registry = new ToolCapabilityRegistry(
            new ToolRegistry(
                new[] { screenTool }));

        var capability = registry.FindByCapability(
                "read")
            .Single();

        Require(
            capability.Name == "computer.screen.info" &&
            capability.RiskLevel == ToolRiskLevels.Low &&
            !capability.RequiresConfirmation &&
            capability.Capabilities.Contains(
                "computer",
                StringComparer.OrdinalIgnoreCase),
            "Read-only computer tool không giữ low-risk/read capability.");
    }

    private static void CheckCapabilityRouterKeepsChannelBoundary()
    {
        IPersonalAiTool screenTool =
            new ComputerScreenInfoTool(
                new AcceptanceComputerUseService());

        IPersonalAiTool browserTool =
            new AcceptanceCapabilityTool(
                "browser.test.read",
                [ToolPermissions.Read, ToolPermissions.Browser],
                requiresConfirmation: false);

        var registry = new ToolCapabilityRegistry(
            new ToolRegistry(
                new[] { screenTool, browserTool }));

        var router = new CapabilityToolRouter(registry);

        var result = router.Route(
            new CapabilityToolRouteRequest(
                ExecutionAgentChannels.Computer,
                ["read"],
                AllowHighRisk: false,
                RequireVerification: false));

        Require(
            result.Tools.Count == 1 &&
            result.Tools[0].Name == "computer.screen.info",
            "Capability router làm lộ tool ngoài channel computer.");
    }

    private static void CheckCapabilityRouterBlocksHighRiskByDefault()
    {
        IPersonalAiTool safeTool =
            new ComputerScreenInfoTool(
                new AcceptanceComputerUseService());

        IPersonalAiTool highRiskTool =
            new ComputerOperatorTaskTool(
                new AcceptanceOperatorTaskService());

        var registry = new ToolCapabilityRegistry(
            new ToolRegistry(
                new[] { safeTool, highRiskTool }));

        var router = new CapabilityToolRouter(registry);

        var defaultRoute = router.Route(
            new CapabilityToolRouteRequest(
                ExecutionAgentChannels.Computer,
                Array.Empty<string>()));

        Require(
            defaultRoute.Tools.All(tool =>
                tool.RiskLevel != ToolRiskLevels.High),
            "Capability router vẫn đưa high-risk tool vào mặc định.");

        var elevatedRoute = router.Route(
            new CapabilityToolRouteRequest(
                ExecutionAgentChannels.Computer,
                ["side-effect"],
                AllowHighRisk: true,
                RequireVerification: true));

        Require(
            elevatedRoute.Tools.Any(tool =>
                tool.Name == "computer.operator.run-task"),
            "Capability router không đưa Computer Operator vào khi đã explicit allow high-risk + verification.");
    }

    private static void CheckBrowserExecutionAgentChannelBoundary()
    {
        var agent = new BrowserExecutionAgent(
            new AcceptanceBrowserExecutionBackend());

        var accepted = agent.CanHandle(
            new ExecutionAgentRequest(
                "Mở https://example.com",
                ExecutionAgentChannels.Browser),
            out var acceptedConfidence,
            out _);

        var rejected = agent.CanHandle(
            new ExecutionAgentRequest(
                "Mở https://example.com",
                ExecutionAgentChannels.Computer),
            out var rejectedConfidence,
            out _);

        Require(
            accepted &&
            acceptedConfidence >= 0.9 &&
            !rejected &&
            rejectedConfidence == 0,
            "Browser execution agent không giữ đúng channel boundary.");
    }

    private static void CheckBrowserExecutionAgentBackendContract()
    {
        var backend = new AcceptanceBrowserExecutionBackend();
        var agent = new BrowserExecutionAgent(backend);

        var result = agent.ExecuteAsync(
                new ExecutionAgentRequest(
                    "Mở https://example.com",
                    ExecutionAgentChannels.Browser))
            .GetAwaiter()
            .GetResult();

        Require(
            backend.CallCount == 1 &&
            result.AgentId == BrowserExecutionAgent.AgentId &&
            result.Status == AgentExecutionStatuses.Succeeded &&
            result.Verified &&
            result.Provider == "browser" &&
            result.Model == "acceptance-browser",
            "Browser execution agent không bọc backend đúng contract.");
    }

    private static void CheckCodingExecutionAgentExplicitCommandBoundary()
    {
        var agent = new CodingExecutionAgent(
            new AcceptanceCodingExecutionBackend());

        var accepted = agent.CanHandle(
            new ExecutionAgentRequest(
                "dotnet-build: src/PersonalAI.Web/PersonalAI.Web.csproj",
                ExecutionAgentChannels.Coding),
            out var acceptedConfidence,
            out _);

        var rejected = agent.CanHandle(
            new ExecutionAgentRequest(
                "hãy tự sửa hết code rồi chạy bất kỳ lệnh nào cần thiết",
                ExecutionAgentChannels.Coding),
            out var rejectedConfidence,
            out _);

        Require(
            accepted &&
            acceptedConfidence >= 0.9 &&
            !rejected &&
            rejectedConfidence < 0.5,
            "Coding execution agent không giữ explicit-command boundary.");
    }

    private static void CheckCodingExecutionAgentBackendContract()
    {
        var backend = new AcceptanceCodingExecutionBackend();
        var agent = new CodingExecutionAgent(backend);

        var result = agent.ExecuteAsync(
                new ExecutionAgentRequest(
                    "dotnet-test: src/PersonalAI.Web/PersonalAI.Web.csproj",
                    ExecutionAgentChannels.Coding))
            .GetAwaiter()
            .GetResult();

        Require(
            backend.CallCount == 1 &&
            result.AgentId == CodingExecutionAgent.AgentId &&
            result.Status == AgentExecutionStatuses.Succeeded &&
            result.Verified &&
            result.Provider == "coding" &&
            result.Model == "acceptance-coding",
            "Coding execution agent không bọc backend đúng contract.");
    }

    private static void CheckOpenHandsExplicitRequestBoundary()
    {
        var server = new AcceptanceOpenHandsAgentServerClient(
            enabled: true);
        var backend = new OpenHandsCodingExecutionBackend(
            server);

        var accepted = backend.TryParse(
            new ExecutionAgentRequest(
                "openhands: C:\\repo | sửa lỗi build",
                ExecutionAgentChannels.Coding),
            out var command,
            out var acceptedConfidence,
            out _);

        var rejected = backend.TryParse(
            new ExecutionAgentRequest(
                "hãy tự sửa project bằng OpenHands",
                ExecutionAgentChannels.Coding),
            out _,
            out var rejectedConfidence,
            out _);

        Require(
            accepted &&
            command is not null &&
            command.Operation == "openhands" &&
            acceptedConfidence >= 0.9 &&
            !rejected &&
            rejectedConfidence == 0,
            "OpenHands backend không giữ explicit-request boundary.");
    }

    private static void CheckOpenHandsConversationContract()
    {
        var server = new AcceptanceOpenHandsAgentServerClient(
            enabled: true);
        var backend = new OpenHandsCodingExecutionBackend(
            server);

        var parsed = backend.TryParse(
            new ExecutionAgentRequest(
                "openhands: C:\\repo | sửa lỗi build",
                ExecutionAgentChannels.Coding),
            out var command,
            out _,
            out _);

        Require(
            parsed && command is not null,
            "OpenHands acceptance command không parse được.");

        var result = backend.ExecuteAsync(
                command!)
            .GetAwaiter()
            .GetResult();

        Require(
            server.StartCount == 1 &&
            server.LastWorkingDirectory == "C:\\repo" &&
            server.LastPrompt == "sửa lỗi build" &&
            result.Success &&
            !result.Verified &&
            result.Engine == "openhands-agent-server" &&
            result.Evidence.Any(value =>
                value.Contains(
                    "conversationId=",
                    StringComparison.OrdinalIgnoreCase)),
            "OpenHands backend không giữ đúng conversation/confirmation contract.");
    }

    private static void CheckCodingVerificationGatePassesAllRequiredSteps()
    {
        var development = new AcceptanceDevelopmentAgentService();
        var gate = new CodingVerificationGate(development);

        var report = gate.VerifyAsync(
                new CodingVerificationRequest(
                    "src/App/App.csproj",
                    ".",
                    "Release"))
            .GetAwaiter()
            .GetResult();

        Require(
            report.Passed &&
            report.Steps.Select(step => step.Name)
                .SequenceEqual(
                    new[]
                    {
                        "restore",
                        "build",
                        "test",
                        "diff-review"
                    }) &&
            development.RestoreCount == 1 &&
            development.BuildCount == 1 &&
            development.TestCount == 1 &&
            development.DiffCount == 1,
            "Coding Verification Gate không chạy đủ restore/build/test/diff.");
    }

    private static void CheckCodingVerificationGateStopsOnBuildFailure()
    {
        var development = new AcceptanceDevelopmentAgentService(
            failBuild: true);
        var gate = new CodingVerificationGate(development);

        var report = gate.VerifyAsync(
                new CodingVerificationRequest(
                    "src/App/App.csproj",
                    ".",
                    "Release"))
            .GetAwaiter()
            .GetResult();

        Require(
            !report.Passed &&
            report.Steps.Count == 2 &&
            report.Steps[0].Name == "restore" &&
            report.Steps[1].Name == "build" &&
            development.TestCount == 0 &&
            development.DiffCount == 0,
            "Coding Verification Gate không fail-fast sau build lỗi.");
    }

    private static void CheckIntegrationArchitectureOwnsComputerOperator()
    {
        var architecture =
            new ExternalIntegrationArchitecture();

        var computer = architecture.Get(
            "ai-ca-nhan.computer-operator");

        Require(
            computer is not null &&
            computer.Strategy == IntegrationStrategies.Own &&
            !computer.Replaceable &&
            computer.Contracts.Contains(
                "IExecutionAgent"),
            "Integration architecture không giữ Computer Operator là core do AI-Ca-Nhan sở hữu.");
    }

    private static void CheckIntegrationArchitectureKeepsExternalAdaptersReplaceable()
    {
        var architecture =
            new ExternalIntegrationArchitecture();

        var external = new[]
        {
            architecture.Get("microsoft.agent-framework"),
            architecture.Get("openhands.agent-server"),
            architecture.Get("ai-ca-nhan.browser")
        };

        Require(
            external.All(item =>
                item is not null &&
                item.Replaceable &&
                item.Strategy is
                    IntegrationStrategies.Adapt or
                    IntegrationStrategies.Reuse),
            "External integration không giữ adapter boundary/replaceability.");
    }

    private static void CheckExecutionGatewayRoutesByChannel()
    {
        IExecutionAgent computer =
            new ComputerOperatorExecutionAgent(
                new AcceptanceOperatorTaskService());
        IExecutionAgent browser =
            new BrowserExecutionAgent(
                new AcceptanceBrowserExecutionBackend());

        var registry = new ExecutionAgentRegistry(
            new[] { computer, browser });
        var gateway = new ExecutionGateway(
            registry);

        var preview = gateway.Preview(
            new ExecutionGatewayRequest(
                "Mở ứng dụng thử nghiệm",
                Channel: ExecutionAgentChannels.Computer));

        Require(
            preview.SelectedAgentId ==
                ComputerOperatorExecutionAgent.AgentId &&
            preview.Candidates.Count == 1 &&
            preview.ConfirmationRequired,
            "Execution Gateway route sai agent/channel.");
    }

    private static void CheckExecutionGatewayRequiresConfirmation()
    {
        IExecutionAgent computer =
            new ComputerOperatorExecutionAgent(
                new AcceptanceOperatorTaskService());

        var registry = new ExecutionAgentRegistry(
            new[] { computer });
        var gateway = new ExecutionGateway(
            registry);

        var rejected = false;

        try
        {
            _ = gateway.ExecuteAsync(
                    new ExecutionGatewayRequest(
                        "Mở ứng dụng thử nghiệm",
                        Channel: ExecutionAgentChannels.Computer,
                        ConfirmExecution: false))
                .GetAwaiter()
                .GetResult();
        }
        catch (AgentValidationException)
        {
            rejected = true;
        }

        Require(
            rejected,
            "Execution Gateway vẫn chạy side effect khi chưa ConfirmExecution.");
    }

    private static void CheckUniversalRouterSelectsBrowserForUrl()
    {
        var registry = new ExecutionAgentRegistry(
            new IExecutionAgent[]
            {
                new AcceptanceExecutionAgent(
                    "execution.browser.acceptance",
                    ExecutionAgentChannels.Browser),
                new AcceptanceExecutionAgent(
                    "execution.coding.acceptance",
                    ExecutionAgentChannels.Coding),
                new AcceptanceExecutionAgent(
                    "execution.computer.acceptance",
                    ExecutionAgentChannels.Computer)
            });

        var gateway = new ExecutionGateway(registry);
        var router = new UniversalTaskRouter(
            registry,
            gateway);

        var route = router.Preview(
            new UniversalTaskRouteRequest(
                "Mở https://example.com và đọc nội dung trang"));

        Require(
            route.SelectedChannel == ExecutionAgentChannels.Browser &&
            !route.NeedsFurtherRouting,
            $"Universal Router không chọn browser cho URL: {route.Reason}");
    }

    private static void CheckUniversalRouterSelectsCodingForCodeTask()
    {
        var registry = new ExecutionAgentRegistry(
            new IExecutionAgent[]
            {
                new AcceptanceExecutionAgent(
                    "execution.browser.acceptance",
                    ExecutionAgentChannels.Browser),
                new AcceptanceExecutionAgent(
                    "execution.coding.acceptance",
                    ExecutionAgentChannels.Coding),
                new AcceptanceExecutionAgent(
                    "execution.computer.acceptance",
                    ExecutionAgentChannels.Computer)
            });

        var router = new UniversalTaskRouter(
            registry,
            new ExecutionGateway(registry));

        var route = router.Preview(
            new UniversalTaskRouteRequest(
                "Sửa code trong repository, chạy dotnet build và test project"));

        Require(
            route.SelectedChannel == ExecutionAgentChannels.Coding &&
            !route.NeedsFurtherRouting,
            $"Universal Router không chọn coding cho code task: {route.Reason}");
    }

    private static void CheckUniversalRouterRejectsAmbiguousGoal()
    {
        var registry = new ExecutionAgentRegistry(
            new IExecutionAgent[]
            {
                new AcceptanceExecutionAgent(
                    "execution.browser.acceptance",
                    ExecutionAgentChannels.Browser),
                new AcceptanceExecutionAgent(
                    "execution.coding.acceptance",
                    ExecutionAgentChannels.Coding),
                new AcceptanceExecutionAgent(
                    "execution.computer.acceptance",
                    ExecutionAgentChannels.Computer)
            });

        var router = new UniversalTaskRouter(
            registry,
            new ExecutionGateway(registry));

        var route = router.Preview(
            new UniversalTaskRouteRequest(
                "Giúp tôi xử lý việc này"));

        Require(
            route.SelectedChannel is null &&
            route.NeedsFurtherRouting,
            "Universal Router vẫn tự đoán channel cho goal mơ hồ.");
    }

    private static void CheckUniversalRouterPrefersLowerCostRoute()
    {
        var registry = new ExecutionAgentRegistry(
            new IExecutionAgent[]
            {
                new AcceptanceExecutionAgent(
                    "execution.browser.acceptance",
                    ExecutionAgentChannels.Browser),
                new AcceptanceExecutionAgent(
                    "execution.computer.acceptance",
                    ExecutionAgentChannels.Computer)
            });

        var router = new UniversalTaskRouter(
            registry,
            new ExecutionGateway(registry));

        var route = router.Preview(
            new UniversalTaskRouteRequest(
                "website web trang web browser desktop màn hình chuột ứng dụng"));

        var browser = route.Candidates.Single(candidate =>
            candidate.Channel == ExecutionAgentChannels.Browser);
        var computer = route.Candidates.Single(candidate =>
            candidate.Channel == ExecutionAgentChannels.Computer);

        Require(
            Math.Abs(browser.Confidence - computer.Confidence) < 0.001 &&
            browser.UtilityScore > computer.UtilityScore &&
            route.SelectedChannel == ExecutionAgentChannels.Browser,
            $"Cost/latency router không ưu tiên browser rẻ hơn: browser={browser.UtilityScore:0.00}; computer={computer.UtilityScore:0.00}; {route.Reason}");
    }

    private static void CheckUniversalRouterUrlRegexRegression()
    {
        var registry = new ExecutionAgentRegistry(
            new IExecutionAgent[]
            {
                new AcceptanceExecutionAgent(
                    "execution.browser.acceptance",
                    ExecutionAgentChannels.Browser),
                new AcceptanceExecutionAgent(
                    "execution.computer.acceptance",
                    ExecutionAgentChannels.Computer)
            });

        var router = new UniversalTaskRouter(
            registry,
            new ExecutionGateway(registry));

        var route = router.Preview(
            new UniversalTaskRouteRequest(
                "Đọc https://site.example/path và tóm tắt trang"));

        var browser = route.Candidates.Single(candidate =>
            candidate.Channel == ExecutionAgentChannels.Browser);

        Require(
            browser.Reason.Contains(
                "explicit-url",
                StringComparison.OrdinalIgnoreCase) &&
            route.SelectedChannel == ExecutionAgentChannels.Browser,
            $"URL regex regression: {browser.Reason}; {route.Reason}");
    }

    private static void CheckUniversalRouterDetectsDirectToolOpportunity()
    {
        IPersonalAiTool browserTool =
            new AcceptanceCapabilityTool(
                "browser.acceptance.read",
                [ToolPermissions.Read, ToolPermissions.Browser],
                requiresConfirmation: false);

        var capabilityRegistry =
            new ToolCapabilityRegistry(
                new ToolRegistry(
                    new[] { browserTool }));

        var agentRegistry =
            new ExecutionAgentRegistry(
                new IExecutionAgent[]
                {
                    new AcceptanceExecutionAgent(
                        "execution.browser.acceptance",
                        ExecutionAgentChannels.Browser),
                    new AcceptanceExecutionAgent(
                        "execution.computer.acceptance",
                        ExecutionAgentChannels.Computer)
                });

        var router = new UniversalTaskRouter(
            agentRegistry,
            new ExecutionGateway(agentRegistry),
            capabilityRegistry);

        var route = router.Preview(
            new UniversalTaskRouteRequest(
                "website web trang web browser desktop màn hình chuột ứng dụng"));

        var browser = route.Candidates.Single(candidate =>
            candidate.Channel == ExecutionAgentChannels.Browser);

        Require(
            browser.DirectToolCount == 1 &&
            browser.PreferredToolName == "browser.acceptance.read" &&
            browser.Reason.Contains(
                "direct-tool:browser.acceptance.read",
                StringComparison.OrdinalIgnoreCase),
            $"Capability-first router không phát hiện direct tool: {browser.Reason}");
    }

    private static void CheckUniversalRouterExcludesComputerOperatorFromDirectTools()
    {
        IPersonalAiTool operatorTool =
            new ComputerOperatorTaskTool(
                new AcceptanceOperatorTaskService());

        var capabilityRegistry =
            new ToolCapabilityRegistry(
                new ToolRegistry(
                    new[] { operatorTool }));

        var agentRegistry =
            new ExecutionAgentRegistry(
                new IExecutionAgent[]
                {
                    new AcceptanceExecutionAgent(
                        "execution.computer.acceptance",
                        ExecutionAgentChannels.Computer)
                });

        var router = new UniversalTaskRouter(
            agentRegistry,
            new ExecutionGateway(agentRegistry),
            capabilityRegistry);

        var route = router.Preview(
            new UniversalTaskRouteRequest(
                "mở ứng dụng desktop bằng chuột và bàn phím"));

        var computer = route.Candidates.Single(candidate =>
            candidate.Channel == ExecutionAgentChannels.Computer);

        Require(
            computer.DirectToolCount == 0 &&
            computer.PreferredToolName is null &&
            !computer.Reason.Contains(
                "direct-tool:computer.operator.run-task",
                StringComparison.OrdinalIgnoreCase),
            "Capability-first router coi Computer Operator fallback là direct tool.");
    }

    private static void CheckToolArgumentPlannerAcceptsValidArguments()
    {
        using var argumentsDocument =
            System.Text.Json.JsonDocument.Parse(
                """{"query":"acceptance"}""");

        IPersonalAiTool tool =
            new AcceptanceSchemaTool(
                "test.search",
                requiredField: "query");

        var provider =
            new AcceptanceFunctionPlanningProvider(
                argumentsDocument.RootElement.Clone());

        var planner = new ToolArgumentPlanner(
            new ToolRegistry(
                new[] { tool }),
            new ToolInputValidator(),
            new AcceptanceAiProviderResolver(
                provider));

        var result = planner.PlanAsync(
                new ToolArgumentPlanRequest(
                    "test.search",
                    "Tìm acceptance"))
            .GetAwaiter()
            .GetResult();

        Require(
            result.Proposed &&
            result.ToolName == "test.search" &&
            result.Arguments is { } arguments &&
            arguments.TryGetProperty(
                "query",
                out var query) &&
            query.GetString() == "acceptance" &&
            !result.RequiresConfirmation &&
            provider.ProposalCount == 1,
            $"Tool Argument Planner từ chối arguments hợp lệ: {result.Reason}");
    }

    private static void CheckToolArgumentPlannerRejectsInvalidArguments()
    {
        using var argumentsDocument =
            System.Text.Json.JsonDocument.Parse(
                """{"invented":"value"}""");

        IPersonalAiTool tool =
            new AcceptanceSchemaTool(
                "test.search",
                requiredField: "query");

        var provider =
            new AcceptanceFunctionPlanningProvider(
                argumentsDocument.RootElement.Clone());

        var planner = new ToolArgumentPlanner(
            new ToolRegistry(
                new[] { tool }),
            new ToolInputValidator(),
            new AcceptanceAiProviderResolver(
                provider));

        var result = planner.PlanAsync(
                new ToolArgumentPlanRequest(
                    "test.search",
                    "Tìm acceptance"))
            .GetAwaiter()
            .GetResult();

        Require(
            !result.Proposed &&
            result.Arguments is null &&
            result.Reason.Contains(
                "schema validator",
                StringComparison.OrdinalIgnoreCase),
            "Tool Argument Planner vẫn chấp nhận arguments sai schema.");
    }

    private static void CheckUniversalDirectToolPathPreparesWithoutExecution()
    {
        using var argumentsDocument =
            System.Text.Json.JsonDocument.Parse(
                """{"query":"acceptance"}""");

        var route = new UniversalTaskRoutePreview(
            "Tìm acceptance",
            [
                new UniversalTaskRouteCandidate(
                    ExecutionAgentChannels.Browser,
                    0.90,
                    0.80,
                    UniversalRouteRisk.Medium,
                    2,
                    2,
                    1,
                    1,
                    "browser.acceptance.read",
                    "direct-tool")
            ],
            ExecutionAgentChannels.Browser,
            NeedsFurtherRouting: false,
            "acceptance-route");

        var router =
            new AcceptanceUniversalTaskRouter(
                route);
        var planner =
            new AcceptanceToolArgumentPlanner(
                new ToolArgumentPlanResult(
                    true,
                    "browser.acceptance.read",
                    argumentsDocument.RootElement.Clone(),
                    RequiresConfirmation: false,
                    [ToolPermissions.Read],
                    "AcceptanceAI",
                    "acceptance-model",
                    "acceptance"));
        var orchestration =
            new AcceptanceToolOrchestrationService();

        var pathService =
            new UniversalDirectToolPath(
                router,
                planner,
                orchestration);

        var result = pathService.PrepareAsync(
                new UniversalExecutionPrepareRequest(
                    "Tìm acceptance"))
            .GetAwaiter()
            .GetResult();

        Require(
            result.Mode ==
                UniversalExecutionModes.DirectToolProposal &&
            result.ToolProposal is not null &&
            result.ToolProposal.ToolName ==
                "browser.acceptance.read" &&
            orchestration.PrepareCount == 1 &&
            orchestration.ExecuteCount == 0,
            "Direct Tool Path đã execute hoặc không tạo proposal đúng hai pha.");
    }

    private static void CheckUniversalDirectToolPathFallsBackWithoutExecuting()
    {
        var route = new UniversalTaskRoutePreview(
            "Mở ứng dụng desktop",
            [
                new UniversalTaskRouteCandidate(
                    ExecutionAgentChannels.Computer,
                    0.90,
                    0.70,
                    UniversalRouteRisk.High,
                    5,
                    5,
                    1,
                    0,
                    null,
                    "no-direct-tool")
            ],
            ExecutionAgentChannels.Computer,
            NeedsFurtherRouting: false,
            "acceptance-route");

        var router =
            new AcceptanceUniversalTaskRouter(
                route);
        var planner =
            new AcceptanceToolArgumentPlanner(
                result: null);
        var orchestration =
            new AcceptanceToolOrchestrationService();

        var pathService =
            new UniversalDirectToolPath(
                router,
                planner,
                orchestration);

        var result = pathService.PrepareAsync(
                new UniversalExecutionPrepareRequest(
                    "Mở ứng dụng desktop"))
            .GetAwaiter()
            .GetResult();

        Require(
            result.Mode ==
                UniversalExecutionModes.ExecutionAgentFallback &&
            result.FallbackChannel ==
                ExecutionAgentChannels.Computer &&
            planner.CallCount == 0 &&
            orchestration.PrepareCount == 0 &&
            orchestration.ExecuteCount == 0,
            "Direct Tool Path tự chạy fallback hoặc gọi planner khi không có direct tool.");
    }

    private static void CheckUniversalFallbackPolicyBlocksDeniedBypass()
    {
        var policy = new UniversalFallbackPolicy();
        var route = BuildAcceptanceRoute(
            ExecutionAgentChannels.Computer);

        var execution = BuildAcceptanceToolExecution(
            ToolExecutionStatuses.Denied,
            success: false,
            error: "confirmation required");

        var decision = policy.Evaluate(
            route,
            execution);

        Require(
            decision.Action ==
                UniversalFallbackActions.Stop &&
            !decision.AllowAgentFallback &&
            decision.FallbackChannel is null,
            "Fallback policy cho phép lách policy denial sang Computer Operator.");
    }

    private static void CheckUniversalFallbackPolicyAllowsTechnicalFallback()
    {
        var policy = new UniversalFallbackPolicy();
        var route = BuildAcceptanceRoute(
            ExecutionAgentChannels.Browser);

        var execution = BuildAcceptanceToolExecution(
            ToolExecutionStatuses.TimedOut,
            success: false,
            error: "timeout");

        var decision = policy.Evaluate(
            route,
            execution);

        Require(
            decision.Action ==
                UniversalFallbackActions.AgentFallback &&
            decision.AllowAgentFallback &&
            decision.FallbackChannel ==
                ExecutionAgentChannels.Browser,
            "Fallback policy không đề xuất agent fallback sau timeout kỹ thuật.");
    }

    private static UniversalTaskRoutePreview BuildAcceptanceRoute(
        string channel) =>
        new(
            "acceptance",
            [
                new UniversalTaskRouteCandidate(
                    channel,
                    0.90,
                    0.80,
                    channel == ExecutionAgentChannels.Computer
                        ? UniversalRouteRisk.High
                        : UniversalRouteRisk.Medium,
                    2,
                    2,
                    1,
                    0,
                    null,
                    "acceptance")
            ],
            channel,
            NeedsFurtherRouting: false,
            "acceptance");

    private static ToolExecutionResponse BuildAcceptanceToolExecution(
        string status,
        bool success,
        string? error) =>
        new(
            Guid.NewGuid(),
            "acceptance.tool",
            status,
            success,
            Output: null,
            error,
            DurationMs: 1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [ToolPermissions.Read],
            [ToolPermissions.Read]);

    private static void CheckUniversalOutcomeRequiresEvidence()
    {
        IPersonalAiTool tool =
            new AcceptanceSchemaTool(
                "browser.acceptance.read",
                requiredField: "query");

        var capabilityRegistry =
            new ToolCapabilityRegistry(
                new ToolRegistry(
                    new[] { tool }));

        var agentRegistry =
            new ExecutionAgentRegistry(
                new IExecutionAgent[]
                {
                    new AcceptanceExecutionAgent(
                        "execution.browser.acceptance",
                        ExecutionAgentChannels.Browser)
                });

        var verifier =
            new UniversalOutcomeVerificationService(
                capabilityRegistry,
                agentRegistry);

        var execution = BuildAcceptanceToolExecution(
            ToolExecutionStatuses.Succeeded,
            success: true,
            error: null);

        execution = execution with
        {
            ToolName = "browser.acceptance.read"
        };

        var verification = verifier.VerifyTool(
            new UniversalToolOutcomeVerificationRequest(
                "Đọc nội dung trang",
                execution));

        var fallback = new UniversalFallbackPolicy()
            .Evaluate(
                BuildAcceptanceRoute(
                    ExecutionAgentChannels.Browser),
                execution,
                verification);

        Require(
            verification.Status ==
                UniversalOutcomeStatuses.NeedsVerification &&
            !verification.GoalAchieved &&
            fallback.Action ==
                UniversalFallbackActions.VerifyOutcome,
            "Tool success vẫn bị coi là goal success khi chưa có evidence.");
    }

    private static void CheckUniversalOutcomeFailedEvidenceReplans()
    {
        IPersonalAiTool tool =
            new AcceptanceSchemaTool(
                "browser.acceptance.read",
                requiredField: "query");

        var capabilityRegistry =
            new ToolCapabilityRegistry(
                new ToolRegistry(
                    new[] { tool }));

        var agentRegistry =
            new ExecutionAgentRegistry(
                new IExecutionAgent[]
                {
                    new AcceptanceExecutionAgent(
                        "execution.browser.acceptance",
                        ExecutionAgentChannels.Browser)
                });

        var verifier =
            new UniversalOutcomeVerificationService(
                capabilityRegistry,
                agentRegistry);

        var execution = BuildAcceptanceToolExecution(
            ToolExecutionStatuses.Succeeded,
            success: true,
            error: null) with
        {
            ToolName = "browser.acceptance.read"
        };

        var verification = verifier.VerifyTool(
            new UniversalToolOutcomeVerificationRequest(
                "Đọc nội dung trang",
                execution,
                new UniversalOutcomeEvidence(
                    "acceptance-verifier",
                    Passed: false,
                    Confidence: 0.95,
                    "Trang chưa tải đúng nội dung yêu cầu.")));

        var fallback = new UniversalFallbackPolicy()
            .Evaluate(
                BuildAcceptanceRoute(
                    ExecutionAgentChannels.Browser),
                execution,
                verification);

        Require(
            verification.Status ==
                UniversalOutcomeStatuses.NotAchieved &&
            verification.IndependentlyVerified &&
            !verification.GoalAchieved &&
            fallback.Action ==
                UniversalFallbackActions.ReplanGoal,
            "Outcome fail không buộc replan goal.");
    }

    private static void CheckUniversalOutcomeVerifiedEvidenceCompletes()
    {
        IPersonalAiTool tool =
            new AcceptanceSchemaTool(
                "browser.acceptance.read",
                requiredField: "query");

        var capabilityRegistry =
            new ToolCapabilityRegistry(
                new ToolRegistry(
                    new[] { tool }));

        var agentRegistry =
            new ExecutionAgentRegistry(
                new IExecutionAgent[]
                {
                    new AcceptanceExecutionAgent(
                        "execution.browser.acceptance",
                        ExecutionAgentChannels.Browser)
                });

        var verifier =
            new UniversalOutcomeVerificationService(
                capabilityRegistry,
                agentRegistry);

        var execution = BuildAcceptanceToolExecution(
            ToolExecutionStatuses.Succeeded,
            success: true,
            error: null) with
        {
            ToolName = "browser.acceptance.read"
        };

        var verification = verifier.VerifyTool(
            new UniversalToolOutcomeVerificationRequest(
                "Đọc nội dung trang",
                execution,
                new UniversalOutcomeEvidence(
                    "acceptance-verifier",
                    Passed: true,
                    Confidence: 0.95,
                    "Trang đã tải và chứa nội dung mục tiêu.")));

        var fallback = new UniversalFallbackPolicy()
            .Evaluate(
                BuildAcceptanceRoute(
                    ExecutionAgentChannels.Browser),
                execution,
                verification);

        Require(
            verification.Status ==
                UniversalOutcomeStatuses.Verified &&
            verification.GoalAchieved &&
            verification.IndependentlyVerified &&
            fallback.Action ==
                UniversalFallbackActions.Complete,
            "Outcome pass đủ mạnh vẫn không complete.");
    }

    private static void CheckVerificationEvidenceRouterPrefersToolVerifier()
    {
        IPersonalAiTool tool =
            new AcceptanceSchemaTool(
                "browser.acceptance.verified",
                requiredField: "query",
                supportsVerification: true);

        var router =
            new UniversalVerificationEvidenceRouter(
                new ToolCapabilityRegistry(
                    new ToolRegistry(
                        new[] { tool })));

        var execution = BuildAcceptanceToolExecution(
            ToolExecutionStatuses.Succeeded,
            success: true,
            error: null) with
        {
            ToolName = "browser.acceptance.verified"
        };

        var result = router.Route(
            new UniversalVerificationEvidenceRouteRequest(
                BuildAcceptanceRoute(
                    ExecutionAgentChannels.Browser),
                execution));

        Require(
            result.Strategy ==
                UniversalVerificationStrategies.LocalToolVerifier &&
            result.CanVerifyAutomatically &&
            !result.RequiresExternalAi &&
            !result.RequiresReadback,
            $"Evidence Router không ưu tiên tool verifier: {result.Reason}");
    }

    private static void CheckVerificationEvidenceRouterUsesBrowserReadback()
    {
        IPersonalAiTool tool =
            new AcceptanceSchemaTool(
                "browser.acceptance.read",
                requiredField: "query");

        var router =
            new UniversalVerificationEvidenceRouter(
                new ToolCapabilityRegistry(
                    new ToolRegistry(
                        new[] { tool })));

        var execution = BuildAcceptanceToolExecution(
            ToolExecutionStatuses.Succeeded,
            success: true,
            error: null) with
        {
            ToolName = "browser.acceptance.read"
        };

        var result = router.Route(
            new UniversalVerificationEvidenceRouteRequest(
                BuildAcceptanceRoute(
                    ExecutionAgentChannels.Browser),
                execution));

        Require(
            result.Strategy ==
                UniversalVerificationStrategies.BrowserReadback &&
            result.CanVerifyAutomatically &&
            !result.RequiresExternalAi &&
            result.RequiresReadback,
            $"Evidence Router không chọn browser readback: {result.Reason}");
    }

    private static void CheckVerificationEvidenceRouterUsesComputerVerifier()
    {
        IPersonalAiTool tool =
            new AcceptanceSchemaTool(
                "computer.acceptance.action",
                requiredField: "goal");

        var router =
            new UniversalVerificationEvidenceRouter(
                new ToolCapabilityRegistry(
                    new ToolRegistry(
                        new[] { tool })));

        var execution = BuildAcceptanceToolExecution(
            ToolExecutionStatuses.Succeeded,
            success: true,
            error: null) with
        {
            ToolName = "computer.acceptance.action"
        };

        var result = router.Route(
            new UniversalVerificationEvidenceRouteRequest(
                BuildAcceptanceRoute(
                    ExecutionAgentChannels.Computer),
                execution));

        Require(
            result.Strategy ==
                UniversalVerificationStrategies.ComputerOperatorVerifier &&
            result.CanVerifyAutomatically &&
            result.RequiresExternalAi &&
            result.RequiresReadback,
            $"Evidence Router không chọn Computer Operator verifier: {result.Reason}");
    }

    private static void CheckVerificationEvidenceAdapterBrowser()
    {
        var adapters =
            new UniversalVerificationEvidenceAdapters();

        var evidence = adapters.FromBrowser(
            new BrowserExecutionBackendResult(
                Success: true,
                Summary: "Đã đọc lại trang đích.",
                Evidence: ["url-match"],
                ChangedExternalState: true,
                Verified: true,
                Engine: "acceptance-browser"));

        Require(
            evidence is not null &&
            evidence.Passed &&
            evidence.Confidence >= 0.90 &&
            evidence.Source ==
                "browser:acceptance-browser",
            "Browser verified không được chuyển thành universal evidence đúng.");
    }

    private static void CheckVerificationEvidenceAdapterCodingFailure()
    {
        var adapters =
            new UniversalVerificationEvidenceAdapters();

        var evidence = adapters.FromCoding(
            new CodingVerificationReport(
                Passed: false,
                [
                    new CodingVerificationStep(
                        "restore",
                        true,
                        "ok"),
                    new CodingVerificationStep(
                        "build",
                        false,
                        "failed")
                ],
                "Build thất bại."));

        Require(
            !evidence.Passed &&
            evidence.Confidence >= 0.90 &&
            evidence.Source ==
                "coding-verification-gate" &&
            evidence.Summary.Contains(
                "1/2",
                StringComparison.Ordinal),
            "Coding verification failure không được giữ nguyên khi adapter hóa.");
    }

    private static void CheckVerificationEvidenceAdapterDesktopFailure()
    {
        var adapters =
            new UniversalVerificationEvidenceAdapters();

        var evidence = adapters.FromDesktop(
            new DesktopVerificationRoutingResult(
                DesktopVerificationRoute.LocalFailed,
                0.96,
                "Không thấy UI thay đổi."));

        Require(
            evidence is not null &&
            !evidence.Passed &&
            Math.Abs(
                evidence.Confidence -
                0.96) < 0.001 &&
            evidence.Source ==
                "desktop-local-verifier",
            "Desktop local failure bị mất khi adapter hóa.");
    }

    private static void CheckVerificationEvidenceAdapterDesktopSemanticFallback()
    {
        var adapters =
            new UniversalVerificationEvidenceAdapters();

        var evidence = adapters.FromDesktop(
            new DesktopVerificationRoutingResult(
                DesktopVerificationRoute.GeminiRequired,
                0,
                "Cần hiểu semantic."));

        Require(
            evidence is null,
            "Adapter đã bịa universal evidence dù Desktop Verification Router yêu cầu Gemini.");
    }

    private static void CheckVerificationEvidenceCollectorBrowserReadback()
    {
        IPersonalAiTool tool =
            new AcceptanceSchemaTool(
                "browser.acceptance.read",
                requiredField: "query");

        var capabilityRegistry =
            new ToolCapabilityRegistry(
                new ToolRegistry(
                    new[] { tool }));

        var evidenceRouter =
            new UniversalVerificationEvidenceRouter(
                capabilityRegistry);

        var browser =
            new AcceptanceBrowserAgentService();

        var codingGate =
            new AcceptanceCodingVerificationGate();

        var collector =
            new UniversalVerificationEvidenceCollectionService(
                evidenceRouter,
                new UniversalVerificationEvidenceAdapters(),
                browser,
                codingGate);

        var execution = BuildAcceptanceToolExecution(
            ToolExecutionStatuses.Succeeded,
            success: true,
            error: null) with
        {
            ToolName = "browser.acceptance.read"
        };

        var result = collector.CollectAsync(
                new UniversalVerificationEvidenceCollectionRequest(
                    BuildAcceptanceRoute(
                        ExecutionAgentChannels.Browser),
                    execution))
            .GetAwaiter()
            .GetResult();

        Require(
            result.Collected &&
            result.Evidence is not null &&
            result.Evidence.Passed &&
            result.Evidence.Source ==
                "browser-structured-readback" &&
            browser.ObserveCount == 1 &&
            codingGate.CallCount == 0,
            $"Browser readback evidence collection sai: {result.Reason}");
    }

    private static void CheckVerificationEvidenceCollectorCodingNeedsContext()
    {
        IPersonalAiTool tool =
            new AcceptanceSchemaTool(
                "development.acceptance.build",
                requiredField: "targetPath");

        var collector =
            new UniversalVerificationEvidenceCollectionService(
                new UniversalVerificationEvidenceRouter(
                    new ToolCapabilityRegistry(
                        new ToolRegistry(
                            new[] { tool }))),
                new UniversalVerificationEvidenceAdapters(),
                new AcceptanceBrowserAgentService(),
                new AcceptanceCodingVerificationGate());

        var execution = BuildAcceptanceToolExecution(
            ToolExecutionStatuses.Succeeded,
            success: true,
            error: null) with
        {
            ToolName = "development.acceptance.build"
        };

        var result = collector.CollectAsync(
                new UniversalVerificationEvidenceCollectionRequest(
                    BuildAcceptanceRoute(
                        ExecutionAgentChannels.Coding),
                    execution))
            .GetAwaiter()
            .GetResult();

        Require(
            !result.Collected &&
            result.Evidence is null &&
            result.Strategy ==
                UniversalVerificationStrategies.CodingVerificationGate &&
            result.Reason.Contains(
                "thiếu context",
                StringComparison.OrdinalIgnoreCase),
            "Coding collector vẫn tự đoán verification path khi thiếu context.");
    }

    private static void CheckVerificationEvidenceCollectorCodingGate()
    {
        IPersonalAiTool tool =
            new AcceptanceSchemaTool(
                "development.acceptance.build",
                requiredField: "targetPath");

        var gate =
            new AcceptanceCodingVerificationGate(
                passed: true);

        var collector =
            new UniversalVerificationEvidenceCollectionService(
                new UniversalVerificationEvidenceRouter(
                    new ToolCapabilityRegistry(
                        new ToolRegistry(
                            new[] { tool }))),
                new UniversalVerificationEvidenceAdapters(),
                new AcceptanceBrowserAgentService(),
                gate);

        var execution = BuildAcceptanceToolExecution(
            ToolExecutionStatuses.Succeeded,
            success: true,
            error: null) with
        {
            ToolName = "development.acceptance.build"
        };

        var result = collector.CollectAsync(
                new UniversalVerificationEvidenceCollectionRequest(
                    BuildAcceptanceRoute(
                        ExecutionAgentChannels.Coding),
                    execution,
                    new CodingVerificationRequest(
                        "src/App/App.csproj",
                        ".",
                        "Release")))
            .GetAwaiter()
            .GetResult();

        Require(
            result.Collected &&
            result.Evidence is not null &&
            result.Evidence.Passed &&
            result.Evidence.Source ==
                "coding-verification-gate" &&
            gate.CallCount == 1,
            $"Coding verification evidence collection sai: {result.Reason}");
    }

    private static void CheckVerificationEvidenceCollectorDesktopSemanticFallback()
    {
        IPersonalAiTool tool =
            new AcceptanceSchemaTool(
                "computer.acceptance.action",
                requiredField: "goal");

        var collector =
            new UniversalVerificationEvidenceCollectionService(
                new UniversalVerificationEvidenceRouter(
                    new ToolCapabilityRegistry(
                        new ToolRegistry(
                            new[] { tool }))),
                new UniversalVerificationEvidenceAdapters(),
                new AcceptanceBrowserAgentService(),
                new AcceptanceCodingVerificationGate());

        var execution = BuildAcceptanceToolExecution(
            ToolExecutionStatuses.Succeeded,
            success: true,
            error: null) with
        {
            ToolName = "computer.acceptance.action"
        };

        var result = collector.CollectAsync(
                new UniversalVerificationEvidenceCollectionRequest(
                    BuildAcceptanceRoute(
                        ExecutionAgentChannels.Computer),
                    execution,
                    DesktopVerification: new DesktopVerificationRoutingResult(
                        DesktopVerificationRoute.GeminiRequired,
                        0,
                        "Cần semantic vision.")))
            .GetAwaiter()
            .GetResult();

        Require(
            !result.Collected &&
            result.Evidence is null &&
            result.Reason.Contains(
                "semantic Vision",
                StringComparison.OrdinalIgnoreCase),
            "Desktop collector đã bịa evidence thay vì chuyển sang semantic verifier.");
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

    private sealed class AcceptanceBrowserAgentService
        : IBrowserAgentService
    {
        public int ObserveCount { get; private set; }

        public BrowserAgentStatusResponse GetStatus() =>
            new(
                "acceptance",
                "acceptance",
                true,
                false,
                false,
                false,
                false,
                1024,
                8000,
                30,
                5,
                [BrowserAgentCapabilities.PageObserve],
                Array.Empty<string>());

        public BrowserSessionInfo GetSessionInfo() =>
            new(
                "acceptance",
                true,
                "https://example.com/",
                "https://example.com",
                "Example",
                200,
                "text/html",
                DateTimeOffset.UtcNow,
                42,
                1,
                1);

        public Task<BrowserNavigationResult> NavigateAsync(
            string url,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Acceptance evidence collector không được navigate.");

        public Task<BrowserNavigationResult> OpenLinkAsync(
            int linkIndex,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Acceptance evidence collector không được open link.");

        public BrowserPageObservation ObservePage(
            int maximumCharacters = BrowserAgentService.MaximumPageTextCharacters,
            int maximumLinks = BrowserAgentService.MaximumLinks)
        {
            ObserveCount++;

            return new(
                "acceptance",
                "https://example.com/",
                "https://example.com",
                "Example",
                200,
                "text/html",
                DateTimeOffset.UtcNow,
                "acceptance page content",
                false,
                [
                    new BrowserPageLink(
                        0,
                        "Example",
                        "https://example.com/about",
                        true)
                ],
                1);
        }
    }

    private sealed class AcceptanceCodingVerificationGate
        : ICodingVerificationGate
    {
        private readonly bool passed;

        public int CallCount { get; private set; }

        public AcceptanceCodingVerificationGate(
            bool passed = true)
        {
            this.passed = passed;
        }

        public Task<CodingVerificationReport> VerifyAsync(
            CodingVerificationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            return Task.FromResult(
                new CodingVerificationReport(
                    passed,
                    [
                        new CodingVerificationStep(
                            "restore",
                            true,
                            "ok"),
                        new CodingVerificationStep(
                            "build",
                            passed,
                            passed ? "ok" : "failed"),
                        new CodingVerificationStep(
                            "test",
                            passed,
                            passed ? "ok" : "skipped"),
                        new CodingVerificationStep(
                            "diff-review",
                            passed,
                            passed ? "ok" : "skipped")
                    ],
                    passed
                        ? "Coding verification gate đã PASS."
                        : "Coding verification gate chưa đạt."));
        }
    }

    private sealed class AcceptanceUniversalTaskRouter
        : IUniversalTaskRouter
    {
        private readonly UniversalTaskRoutePreview preview;

        public AcceptanceUniversalTaskRouter(
            UniversalTaskRoutePreview preview)
        {
            this.preview = preview;
        }

        public UniversalTaskRoutePreview Preview(
            UniversalTaskRouteRequest request) =>
            preview;

        public Task<UniversalTaskRouteExecution> ExecuteAsync(
            UniversalTaskRouteRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Acceptance direct-tool prepare không được gọi router execute.");
    }

    private sealed class AcceptanceToolArgumentPlanner
        : IToolArgumentPlanner
    {
        private readonly ToolArgumentPlanResult? result;

        public int CallCount { get; private set; }

        public AcceptanceToolArgumentPlanner(
            ToolArgumentPlanResult? result)
        {
            this.result = result;
        }

        public Task<ToolArgumentPlanResult> PlanAsync(
            ToolArgumentPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            return result is null
                ? throw new InvalidOperationException(
                    "Planner không được gọi trong fallback acceptance test.")
                : Task.FromResult(result);
        }
    }

    private sealed class AcceptanceToolOrchestrationService
        : IToolOrchestrationService
    {
        public int PrepareCount { get; private set; }
        public int ExecuteCount { get; private set; }

        public Task<ToolCallProposal?> ProposeAsync(
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ToolCallProposal?>(null);

        public ToolCallProposal Prepare(
            ToolProposalDraft draft)
        {
            PrepareCount++;

            return new ToolCallProposal(
                Guid.NewGuid(),
                draft.ToolName,
                draft.Arguments.Clone(),
                draft.Reason ?? "acceptance",
                draft.AssistantMessage ?? "acceptance",
                [ToolPermissions.Read],
                RequiresConfirmation: false,
                DateTimeOffset.UtcNow.AddMinutes(10),
                PlanningMode: "acceptance");
        }

        public Task<ToolProposalExecutionResponse?> ExecuteAsync(
            Guid proposalId,
            bool confirmed,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return Task.FromResult<ToolProposalExecutionResponse?>(null);
        }

        public Task<ToolResultSynthesisResponse?> SynthesizeAsync(
            Guid invocationId,
            bool confirmedExternal,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ToolResultSynthesisResponse?>(null);

        public Task<ToolNativeContinuationResponse?> ContinueNativeAsync(
            Guid invocationId,
            bool confirmedExternal,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ToolNativeContinuationResponse?>(null);
    }

    private sealed class AcceptanceAiProviderResolver
        : IAiProviderResolver
    {
        private readonly IAiProvider provider;

        public AcceptanceAiProviderResolver(
            IAiProvider provider)
        {
            this.provider = provider;
        }

        public IAiProvider GetActive() =>
            provider;

        public IAiProvider GetByName(
            string providerName) =>
            provider;
    }

    private sealed class AcceptanceFunctionPlanningProvider
        : IAiProvider
    {
        private readonly System.Text.Json.JsonElement arguments;

        public int ProposalCount { get; private set; }

        public AcceptanceFunctionPlanningProvider(
            System.Text.Json.JsonElement arguments)
        {
            this.arguments = arguments.Clone();
        }

        public string Name => "AcceptanceAI";
        public string Model => "acceptance-model";
        public bool IsConfigured => true;

        public Task<string> ReplyAsync(
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken) =>
            Task.FromResult("acceptance");

        public Task<ProviderFunctionCallDecision?> ProposeFunctionCallAsync(
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<ProviderFunctionDefinition> functions,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProposalCount++;

            var function = functions.Single();
            var context =
                new ProviderFunctionCallContext(
                    Name,
                    Model,
                    function.Name,
                    ResponseId: null,
                    CallId: "acceptance-call",
                    arguments,
                    messages,
                    function);

            return Task.FromResult<ProviderFunctionCallDecision?>(
                new ProviderFunctionCallDecision(
                    function.Name,
                    arguments,
                    context));
        }

        public Task<string> ContinueFunctionCallAsync(
            ProviderFunctionCallContext context,
            string toolResultPayload,
            CancellationToken cancellationToken) =>
            Task.FromResult("acceptance");
    }

    private sealed class AcceptanceSchemaTool
        : IPersonalAiTool
    {
        public ToolDefinition Definition { get; }

        public AcceptanceSchemaTool(
            string name,
            string requiredField,
            bool supportsVerification = false)
        {
            var schemaText =
                "{\"type\":\"object\",\"properties\":{\"" +
                requiredField +
                "\":{\"type\":\"string\",\"minLength\":1}},\"required\":[\"" +
                requiredField +
                "\"],\"additionalProperties\":false}";

            using var schema =
                System.Text.Json.JsonDocument.Parse(
                    schemaText);

            Definition = new ToolDefinition(
                name,
                "Acceptance schema tool.",
                "1.0.0",
                [ToolPermissions.Read],
                1000,
                schema.RootElement.Clone(),
                LocalOnly: true,
                RequiresConfirmation: false,
                SupportsVerification: supportsVerification);
        }

        public Task<System.Text.Json.JsonElement> ExecuteAsync(
            System.Text.Json.JsonElement arguments,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                System.Text.Json.JsonSerializer.SerializeToElement(
                    new { ok = true }));
    }

    private sealed class AcceptanceExecutionAgent
        : IExecutionAgent
    {
        public ExecutionAgentDefinition Definition { get; }

        public AcceptanceExecutionAgent(
            string id,
            string channel)
        {
            Definition = new ExecutionAgentDefinition(
                id,
                id,
                "Acceptance execution agent.",
                ["acceptance"],
                [channel],
                HasSideEffects: true,
                RequiresExplicitInvocation: true,
                SupportsVerification: true,
                SupportsRecovery: false);
        }

        public bool CanHandle(
            ExecutionAgentRequest request,
            out double confidence,
            out string reason)
        {
            var accepted = Definition.Channels.Any(channel =>
                channel.Equals(
                    request.Channel,
                    StringComparison.OrdinalIgnoreCase));

            confidence = accepted
                ? 0.99
                : 0;
            reason = accepted
                ? "acceptance"
                : "wrong-channel";
            return accepted;
        }

        public Task<ExecutionAgentResult> ExecuteAsync(
            ExecutionAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                new ExecutionAgentResult(
                    Definition.Id,
                    AgentExecutionStatuses.Succeeded,
                    "acceptance",
                    ["acceptance"],
                    ChangedExternalState: true,
                    Verified: true,
                    Provider: "acceptance",
                    Model: "acceptance"));
        }
    }

    private sealed class AcceptanceDevelopmentAgentService
        : IDevelopmentAgentService
    {
        private readonly bool failBuild;

        public int RestoreCount { get; private set; }
        public int BuildCount { get; private set; }
        public int TestCount { get; private set; }
        public int DiffCount { get; private set; }

        public AcceptanceDevelopmentAgentService(
            bool failBuild = false)
        {
            this.failBuild = failBuild;
        }

        public DevelopmentStatusResponse GetStatus() =>
            new(
                "acceptance",
                "acceptance",
                true,
                true,
                false,
                false,
                true,
                100,
                100,
                1000,
                Array.Empty<string>(),
                Array.Empty<string>());

        public DevelopmentWorkspaceInspection InspectWorkspace() =>
            new(
                "acceptance",
                0,
                false,
                Array.Empty<DevelopmentProjectEntry>(),
                Array.Empty<string>(),
                Array.Empty<string>());

        public Task<DevelopmentSearchResult> SearchTextAsync(
            string query,
            bool caseSensitive,
            int maximumHits,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new DevelopmentSearchResult(
                    "acceptance",
                    query,
                    0,
                    0,
                    false,
                    Array.Empty<DevelopmentSearchHit>()));

        public Task<DevelopmentGitResult> GitStatusAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Git("status"));

        public Task<DevelopmentGitResult> GitDiffAsync(
            string repositoryPath,
            bool staged,
            CancellationToken cancellationToken = default)
        {
            DiffCount++;
            return Task.FromResult(
                Git("diff"));
        }

        public Task<DevelopmentBranchResult> GetCurrentBranchAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new DevelopmentBranchResult(
                    repositoryPath,
                    "experiment/acceptance",
                    true,
                    1,
                    string.Empty));

        public Task<DevelopmentBranchResult> CreateExperimentBranchAsync(
            string repositoryPath,
            string baseBranch,
            string experimentBranch,
            bool confirmed,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new DevelopmentBranchResult(
                    repositoryPath,
                    experimentBranch,
                    true,
                    1,
                    string.Empty));

        public Task<DevelopmentProcessResult> DotnetRestoreAsync(
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            RestoreCount++;
            return Task.FromResult(
                Process(
                    "dotnet restore",
                    targetPath,
                    succeeded: true));
        }

        public Task<DevelopmentProcessResult> DotnetBuildAsync(
            string targetPath,
            string configuration,
            CancellationToken cancellationToken = default)
        {
            BuildCount++;
            return Task.FromResult(
                Process(
                    "dotnet build",
                    targetPath,
                    succeeded: !failBuild));
        }

        public Task<DevelopmentProcessResult> DotnetTestAsync(
            string targetPath,
            string configuration,
            CancellationToken cancellationToken = default)
        {
            TestCount++;
            return Task.FromResult(
                Process(
                    "dotnet test",
                    targetPath,
                    succeeded: true));
        }

        public Task<DevelopmentPublishResult> DotnetPublishCandidateAsync(
            string targetPath,
            string configuration,
            string deploymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new DevelopmentPublishResult(
                    deploymentId,
                    targetPath,
                    0,
                    true,
                    false,
                    1,
                    string.Empty,
                    false,
                    0,
                    0));

        public Task<DevelopmentDeploymentRollbackResult> DiscardDeploymentCandidateAsync(
            string deploymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new DevelopmentDeploymentRollbackResult(
                    deploymentId,
                    true,
                    true,
                    DateTimeOffset.UtcNow));

        private static DevelopmentProcessResult Process(
            string tool,
            string targetPath,
            bool succeeded) =>
            new(
                tool,
                targetPath,
                succeeded ? 0 : 1,
                succeeded,
                false,
                1,
                succeeded ? "ok" : "failed",
                false);

        private static DevelopmentGitResult Git(
            string command) =>
            new(
                command,
                0,
                true,
                1,
                "diff",
                false);
    }

    private sealed class AcceptanceOpenHandsAgentServerClient
        : IOpenHandsAgentServerClient
    {
        private readonly bool enabled;

        public int StartCount { get; private set; }
        public string LastWorkingDirectory { get; private set; } = string.Empty;
        public string LastPrompt { get; private set; } = string.Empty;

        public AcceptanceOpenHandsAgentServerClient(
            bool enabled)
        {
            this.enabled = enabled;
        }

        public OpenHandsAgentServerOptions GetOptions() =>
            new(
                enabled,
                "http://127.0.0.1:8000",
                "acceptance-model",
                string.Empty,
                20,
                false);

        public Task<OpenHandsAgentServerStatus> GetStatusAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                new OpenHandsAgentServerStatus(
                    enabled,
                    Configured: true,
                    Reachable: enabled,
                    Ready: enabled,
                    "http://127.0.0.1:8000",
                    "acceptance-model",
                    "acceptance",
                    "acceptance"));
        }

        public Task<OpenHandsConversationStartResult> StartCodingConversationAsync(
            string workingDirectory,
            string prompt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            StartCount++;
            LastWorkingDirectory = workingDirectory;
            LastPrompt = prompt;

            return Task.FromResult(
                new OpenHandsConversationStartResult(
                    true,
                    "00000000-0000-0000-0000-000000000001",
                    "waiting_for_confirmation",
                    "AlwaysConfirm"));
        }
    }

    private sealed class AcceptanceCodingExecutionBackend
        : ICodingExecutionBackend
    {
        public int CallCount { get; private set; }

        public string Engine => "acceptance-coding";

        public bool TryParse(
            ExecutionAgentRequest request,
            out CodingExecutionCommand? command,
            out double confidence,
            out string reason)
        {
            command = null;

            if (!request.Channel.Equals(
                    ExecutionAgentChannels.Coding,
                    StringComparison.OrdinalIgnoreCase))
            {
                confidence = 0;
                reason = "wrong-channel";
                return false;
            }

            var goal = request.Goal ?? string.Empty;
            if (!goal.StartsWith(
                    "dotnet-",
                    StringComparison.OrdinalIgnoreCase))
            {
                confidence = 0.30;
                reason = "explicit-command-required";
                return false;
            }

            command = new CodingExecutionCommand(
                CodingExecutionOperations.DotnetTest,
                "acceptance.csproj");
            confidence = 0.99;
            reason = "acceptance";
            return true;
        }

        public Task<CodingExecutionBackendResult> ExecuteAsync(
            CodingExecutionCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            return Task.FromResult(
                new CodingExecutionBackendResult(
                    true,
                    "acceptance coding result",
                    ["verified"],
                    ChangedExternalState: true,
                    Verified: true,
                    Engine));
        }
    }

    private sealed class AcceptanceBrowserExecutionBackend
        : IBrowserExecutionBackend
    {
        public int CallCount { get; private set; }

        public string Engine => "acceptance-browser";

        public bool CanHandle(
            ExecutionAgentRequest request,
            out double confidence,
            out string reason)
        {
            var accepted =
                request.Channel.Equals(
                    ExecutionAgentChannels.Browser,
                    StringComparison.OrdinalIgnoreCase) &&
                (request.Goal ?? string.Empty).Contains(
                    "http",
                    StringComparison.OrdinalIgnoreCase);

            confidence = accepted ? 0.99 : 0;
            reason = accepted
                ? "acceptance"
                : "rejected";
            return accepted;
        }

        public Task<BrowserExecutionBackendResult> ExecuteAsync(
            ExecutionAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            return Task.FromResult(
                new BrowserExecutionBackendResult(
                    true,
                    "acceptance browser result",
                    ["verified"],
                    ChangedExternalState: true,
                    Verified: true,
                    Engine));
        }
    }

    private sealed class AcceptanceCapabilityTool
        : IPersonalAiTool
    {
        public ToolDefinition Definition { get; }

        public AcceptanceCapabilityTool(
            string name,
            IReadOnlyList<string> permissions,
            bool requiresConfirmation)
        {
            using var document = System.Text.Json.JsonDocument.Parse(
                """{"type":"object","properties":{},"additionalProperties":false}""");

            Definition = new ToolDefinition(
                name,
                "Acceptance capability tool.",
                "1.0.0",
                permissions,
                1000,
                document.RootElement.Clone(),
                LocalOnly: true,
                RequiresConfirmation: requiresConfirmation);
        }

        public Task<System.Text.Json.JsonElement> ExecuteAsync(
            System.Text.Json.JsonElement arguments,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                System.Text.Json.JsonSerializer.SerializeToElement(
                    new { ok = true }));
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
