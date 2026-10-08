using System.Text.Json;
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

public sealed class ComputerOperatorAcceptanceService(
    IToolRegistry toolRegistry)
    : IComputerOperatorAcceptanceService
{
    public ComputerOperatorAcceptanceResponse Run()
    {
        var checks = new List<ComputerOperatorAcceptanceCheck>();

        RunCheck(
            checks,
            "capture backend router giữ thứ tự WGC → DXGI → PrintWindow → CopyFromScreen",
            CheckCaptureBackendRouterOrder);

        RunCheck(
            checks,
            "capture backend router chỉ chọn backend đã khả dụng",
            CheckCaptureBackendRouterAvailableFallback);

        RunCheck(
            checks,
            "capture health phát hiện chuỗi frame lặp và giữ latency telemetry",
            CheckCaptureHealthDetectsRepeatedFrames);

        RunCheck(
            checks,
            "capture router tự hạ ưu tiên backend bị stale theo đúng target",
            CheckCaptureRouterDeprioritizesStaleBackend);

        RunCheck(
            checks,
            "local visual dHash phân biệt frame giống và khác cấu trúc",
            CheckLocalVisualDifferenceHash);

        RunCheck(
            checks,
            "Windows OCR parser giữ text language và word bounding boxes",
            CheckWindowsOcrPayloadParsing);

        RunCheck(
            checks,
            "OCR resolver chọn duy nhất text target rõ ràng",
            CheckOcrResolverChoosesUniqueTarget);

        RunCheck(
            checks,
            "OCR resolver từ chối target mơ hồ",
            CheckOcrResolverRejectsAmbiguousTarget);

        RunCheck(
            checks,
            "OCR planner map capture box sang frame đúng tỉ lệ",
            CheckOcrPlannerMapsCaptureBoxToFrame);

        RunCheck(
            checks,
            "OCR provider router ưu tiên Windows và chỉ fallback khi cần",
            CheckOcrProviderRouterFallbackOrder);

        RunCheck(
            checks,
            "PaddleOCR worker failure được cô lập khỏi Web host",
            CheckPaddleWorkerInvocationFailureIsIsolated);

        RunCheck(
            checks,
            "local vision worker timeout luôn bị giới hạn an toàn",
            CheckLocalVisionWorkerTimeoutBounds);

        RunCheck(
            checks,
            "local vision warm worker recycle theo request age và idle",
            CheckLocalVisionWorkerRecyclePolicy);

        RunCheck(
            checks,
            "provider cooldown không chụp lặp quá nhanh trên cùng scene",
            CheckProviderCooldownDelayBounds);

        RunCheck(
            checks,
            "Gemini operator dùng structured output schema",
            CheckGeminiOperatorStructuredSchema);

        RunCheck(
            checks,
            "v4.5 minimal intent schema không mang full planner payload",
            CheckMinimalIntentSchema);

        RunCheck(
            checks,
            "v4.5 unified decision kernel compile đúng một action an toàn",
            CheckUnifiedDecisionKernelCompilesSafeAction);

        RunCheck(
            checks,
            "v4.5 kernel từ chối side-effect thiếu expected effect",
            CheckUnifiedDecisionKernelRejectsUnsafeSideEffect);

        RunCheck(
            checks,
            "v4.5.2 visual scene identity đổi khi perceptual hash đổi",
            CheckVisualSceneIdentityIncludesPerceptualHash);

        RunCheck(
            checks,
            "scene fingerprint luôn là SHA-256 hex 64 ký tự trước khi vào Procedure Context/Graph",
            CheckDesktopSceneFingerprintContract);

        RunCheck(
            checks,
            "v4.5.2 verification conflict chuyển sang reobserve",
            CheckVerificationConflictRequiresReobserve);

        RunCheck(
            checks,
            "v4.4.10 Unified Verification Engine gom quyền kết luận local/semantic",
            CheckUnifiedVerificationEngineAuthority);

        RunCheck(
            checks,
            "verification provider exhaustion không được làm chết task khi có thể reobserve",
            CheckVisionProviderExhaustionIsRecoverable);

        RunCheck(
            checks,
            "v4.5.3 chặn replay side-effect ngay sau transition",
            CheckPostTransitionSuppression);

        RunCheck(
            checks,
            "v4.4.8 Decision Authority là nơi duy nhất quyết định execute/replan từ loop/recovery signals",
            CheckSingleDecisionAuthority);

        RunCheck(
            checks,
            "v4.6 semantic target label rút gọn cho local grounding",
            CheckDeterministicTargetGroundingLabels);

        RunCheck(
            checks,
            "v4.6 Windows Search bắt buộc deterministic text grounding",
            CheckWindowsSearchUsesDeterministicGrounding);

        RunCheck(
            checks,
            "v4.4.9 unified grounding cho phép planner visual fallback khi OCR unavailable",
            CheckUnifiedGroundingPlannerFallback);

        RunCheck(
            checks,
            "v4.6 native app launch không chấp nhận browser identity",
            CheckNativeAppLaunchRejectsBrowserIdentity);

        RunCheck(
            checks,
            "local visual provider health cooldown sau failure lặp",
            CheckLocalVisualProviderHealthCooldown);

        RunCheck(
            checks,
            "local visual provider health reset sau success",
            CheckLocalVisualProviderHealthResetsOnSuccess);

        RunCheck(
            checks,
            "OCR provider ảnh trống không bị tính là health failure",
            CheckOcrProviderEmptyResultDoesNotTripHealth);

        RunCheck(
            checks,
            "sensor budget bỏ visual khi structured target đã rõ",
            CheckSensorBudgetSkipsVisualWhenStructuredResolved);

        RunCheck(
            checks,
            "sensor budget cho PaddleOCR khi intent text rõ và UIA yếu",
            CheckSensorBudgetAllowsPaddleForWeakStructuredCoverage);

        RunCheck(
            checks,
            "sensor budget bỏ provider có latency lịch sử quá cao",
            CheckSensorBudgetRejectsHistoricallySlowProvider);

        RunCheck(
            checks,
            "provider health dùng EWMA để giảm nhiễu latency một lần",
            CheckProviderHealthUsesLatencyEwma);

        RunCheck(
            checks,
            "v4.8 runtime regression candidate chỉ lưu fingerprint và failure signal",
            CheckRuntimeRegressionCandidatePrivacy);

        RunCheck(
            checks,
            "v4.8.4 regression draft bắt buộc review và không lộ raw goal",
            CheckRuntimeRegressionPromotionDraft);

        RunCheck(
            checks,
            "v4.8.5 promote regression bắt buộc review và tạo case đã đánh dấu reviewed",
            CheckRuntimeRegressionReviewedPromotion);

        RunCheck(
            checks,
            "v4.8.6 reliability metrics chặn external benchmark khi còn candidate chưa review",
            CheckRegressionReliabilityMetricsReadiness);

        RunCheck(
            checks,
            "v4.8.7 readiness gate chỉ PASS khi regression sạch tuyệt đối",
            CheckRegressionReadinessGate);

        RunCheck(
            checks,
            "v4.9.0 external benchmark chỉ chuẩn bị khi readiness PASS và case hợp lệ",
            CheckExternalBenchmarkPreparationContract);

        RunCheck(
            checks,
            "v4.9.1 scenario pack có 6 case an toàn, duy nhất và installer bắt buộc xác nhận",
            CheckExternalBenchmarkScenarioPackContract);

        RunCheck(
            checks,
            "v4.9.2 benchmark runner chỉ chạy khi xác nhận + interactive desktop và chưa tự chấm PASS",
            CheckExternalBenchmarkRunnerContract);

        RunCheck(
            checks,
            "v4.9.3 independent benchmark evaluation không dùng task completed làm PASS một mình",
            CheckExternalBenchmarkIndependentEvaluationContract);

        RunCheck(
            checks,
            "v4.9.4 runtime state intelligence phân biệt tiến triển, chờ, treo và lỗi bằng bằng chứng",
            CheckRuntimeStateIntelligenceContract);

        RunCheck(
            checks,
            "v4.9.5 progress intelligence hợp nhất nhiều tín hiệu và Adaptive Wait không PASS chỉ vì resource activity",
            CheckProgressIntelligenceContract);

        RunCheck(
            checks,
            "v4.9.6 learned timing dùng percentile an toàn và chỉ thay policy khi đủ mẫu",
            CheckLearnedTimingContract);

        RunCheck(
            checks,
            "v4.9.7 experience database dùng SQLite WAL, workspace isolation và chỉ lưu fingerprint kỹ thuật",
            CheckExperienceDatabaseFoundation);

        RunCheck(
            checks,
            "v4.9.8 chỉ tạo learning candidate sau failure thật -> recovery -> VERIFY PASS",
            CheckVerifiedRecoveryLearningCandidate);

        RunCheck(
            checks,
            "v4.9.9 experience validator không promote sau một lần và chỉ trust khi recovery lặp lại đủ mạnh",
            CheckExperienceValidatorPromotion);

        RunCheck(
            checks,
            "v4.9.10 consolidation gom trusted experience và timing profile vẫn tồn tại sau restart",
            CheckExperienceConsolidationAndPersistentTiming);

        RunCheck(
            checks,
            "v4.9.11 retention xóa candidate rác, archive raw history và chỉ archive trusted experience đã consolidate",
            CheckExperienceRetentionArchiveCleanup);

        RunCheck(
            checks,
            "v4.9.12 verified transition memory giữ các bước A-B đã đúng dù bước C về sau thất bại",
            CheckVerifiedTransitionMemoryKeepsPartialSuccess);

        RunCheck(
            checks,
            "v4.9.13 procedure graph giữ fragment đã đúng và cho phép học nhánh mới từ cùng verified state",
            CheckProcedureGraphPartialReuseAndBranching);

        RunCheck(
            checks,
            "v4.9.14 checkpoint resume re-observe state, join graph giữa chừng và không replay action cuối",
            CheckGraphAwarePartialResume);

        RunCheck(
            checks,
            "v4.9.15 fragment retrieval tìm lại đoạn workflow từ state giữa và không đi vòng graph",
            CheckProcedureFragmentRetrieval);

        RunCheck(
            checks,
            "v4.9.16 strategy ranker chỉ cho Fast Path khi exact state + strategy đủ mạnh và action nằm trong allowlist",
            CheckStrategyRankerFastPathGate);

        RunCheck(
            checks,
            "v4.9.17 procedure versioning nhận cùng family khác UI version, tái dùng fragment có hạ confidence và không Fast Path",
            CheckProcedureVersioningAndContextCompatibility);

        RunCheck(
            checks,
            "v4.9.18 decay giảm trọng số knowledge cũ và supersession thay strategy yếu mà không xóa lịch sử",
            CheckProcedureDecayAndSupersession);

        RunCheck(
            checks,
            "v4.9.19 recovery coordinator hợp nhất runtime, loop, recovery và fragment thành một quyết định ưu tiên",
            CheckRecoveryCoordinatorPriority);

        RunCheck(
            checks,
            "v4.9.20 MCP chỉ là boundary local qua capability router + execution gateway, không bypass Computer Operator core",
            CheckMcpCapabilityBoundary);

        RunCheck(
            checks,
            "v4.9.21 Gemini dùng Polly timeout + circuit breaker nhưng giữ retry hiện tại và không bypass Operator recovery",
            CheckGeminiPollyResilienceBoundary);

        RunCheck(
            checks,
            "Gemini tách circuit breaker chat, tool planning và continuation để lỗi một lane không khóa toàn hệ thống",
            CheckGeminiResilienceLaneIsolation);

        RunCheck(
            checks,
            "v4.9.21 routing fix dùng provider-native Function Calling thay keyword/hard-code app để chọn Computer Operator",
            CheckNativeToolIntentArbitration);

        RunCheck(
            checks,
            "v4.9.21 function planner ưu tiên tool cho yêu cầu hành động nhưng vẫn giữ confirmation/safety",
            CheckFunctionPlannerActionPreference);

        RunCheck(
            checks,
            "tool selection bind vào latest user intent, không bị lệnh cũ hoặc assistant history làm sai target",
            CheckFunctionPlannerIgnoresStaleAssistantCapabilityClaims);

        RunCheck(
            checks,
            "Tool Registry không expose League app-specific route; tác vụ nhiều bước phải dùng Computer Operator tổng quát",
            CheckToolRegistryUsesGeneralComputerOperator);

        RunCheck(
            checks,
            "AI Operator Console chỉ coi BN_CLICKED thật là lệnh nút, không dừng vì focus notification",
            CheckOperatorConsoleIgnoresNonClickButtonNotifications);

        RunCheck(
            checks,
            "primitive con tham gia Operator session hiện tại và không hủy token task cha",
            CheckNestedToolJoinsExistingOperatorSession);

        RunCheck(
            checks,
            "computer.app.launch là wrapper và không được mở Operator session bên ngoài RunAsync",
            CheckAppLaunchWrapperDoesNotOwnOuterOperatorSession);

        RunCheck(
            checks,
            "OpenCV template sensor giữ multi-scale nhỏ và ngưỡng confidence an toàn",
            CheckOpenCvTemplateSensorPolicy);

        RunCheck(
            checks,
            "visual target persistence chỉ nhận ROI hợp lệ và kích thước giới hạn",
            CheckVisualTargetPersistenceTemplateBounds);

        RunCheck(
            checks,
            "local visual fusion tăng confidence khi OCR và OpenCV cùng vùng",
            CheckLocalVisualFusionAgreesOnSameTarget);

        RunCheck(
            checks,
            "local visual fusion từ chối hai nguồn mạnh xung đột vị trí",
            CheckLocalVisualFusionRejectsConflictingTargets);

        RunCheck(
            checks,
            "local visual verification xác nhận thay đổi khi dHash và region cùng mạnh",
            CheckLocalVisualVerificationDetectsChange);

        RunCheck(
            checks,
            "local visual verification xác nhận frame ổn định khi hai cảm biến cùng yên",
            CheckLocalVisualVerificationDetectsStable);



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
            "verification router xác minh local transition khi action khởi chạy app",
            CheckVerificationRouterLaunchTransitionLocalPass);

        RunCheck(
            checks,
            "Gemini planning repair chỉ đóng JSON envelope bị cắt an toàn",
            CheckGeminiPlanningRepairsOnlySafeTruncation);

        RunCheck(
            checks,
            "Gemini malformed preview bị giới hạn và loại base64",
            CheckGeminiMalformedPreviewIsBoundedAndRedacted);

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
            "adaptive Gemini bỏ qua model khi keyboard transition có hai bằng chứng local",
            CheckAdaptiveGeminiSkipsKeyboardTransition);

        RunCheck(
            checks,
            "local planner dùng Windows Search generic khi provider degraded",
            CheckLocalPlannerUsesGenericWindowsSearch);

        RunCheck(
            checks,
            "local planner hiểu goal wrapper computer.app.launch như intent mở ứng dụng",
            CheckLocalPlannerUnderstandsAppLaunchWrapperGoal);

        RunCheck(
            checks,
            "local planner dùng Enter deterministic sau khi wrapper app-launch đã nhập Search",
            CheckLocalPlannerUsesEnterForAppLaunchWrapperAfterTyping);

        RunCheck(
            checks,
            "local planner hiểu goal nhiều bước dạng thực hiện quy trình mở app",
            CheckLocalPlannerUnderstandsWorkflowOpenGoal);

        RunCheck(
            checks,
            "local planner dùng Enter khi Text Engine đã verify text trong Search session hiện tại",
            CheckLocalPlannerUsesEnterAfterVerifiedTextEngineSearch);

        RunCheck(
            checks,
            "local planner cắt intent mở app tại ranh giới câu",
            CheckLocalPlannerStopsOpenTargetAtSentenceBoundary);

        RunCheck(
            checks,
            "local planner nhường quyền sau loop directive",
            CheckLocalPlannerYieldsAfterLoopDirective);

        RunCheck(
            checks,
            "local planner nhường quyền ngay sau Search Enter launch thất bại",
            CheckLocalPlannerYieldsAfterFailedSearchLaunch);

        RunCheck(
            checks,
            "local planner chờ launch có giới hạn sau Search Enter đã xác minh",
            CheckLocalPlannerWaitsAfterVerifiedSearchLaunch);

        RunCheck(
            checks,
            "local planner không mở lại Search khi foreground mới xuất hiện sau launch đã xác minh",
            CheckLocalPlannerYieldsOnTransitionalForegroundAfterLaunch);

        RunCheck(
            checks,
            "Search text marker chỉ hợp lệ trong đúng phiên Search hiện tại",
            CheckLocalPlannerScopesTypedMarkerToCurrentSearchSession);

        RunCheck(
            checks,
            "Computer Operator vision fallback cho HTTP 400 sang provider khác thay vì làm chết task ngay",
            CheckComputerOperatorVisionFallbackIncludesBadRequest);


        RunCheck(
            checks,
            "structured-first planner chọn UIA target duy nhất trước Vision",
            CheckStructuredFirstPlannerUsesUiaTarget);

        RunCheck(
            checks,
            "structured-first planner dùng ValuePattern cho field rõ ràng",
            CheckStructuredFirstPlannerUsesValuePattern);

        RunCheck(
            checks,
            "structured-first planner chọn đúng pattern theo intent",
            CheckStructuredPlannerUsesIntentSpecificPattern);

        RunCheck(
            checks,
            "structured toggle không đảo ngược trạng thái đã đúng",
            CheckStructuredToggleUsesCurrentState);

        RunCheck(
            checks,
            "structured invoke fallback LegacyIAccessible khi thiếu InvokePattern",
            CheckStructuredInvokeFallsBackToLegacyAccessible);

        RunCheck(
            checks,
            "structured verifier xác minh Toggle bằng state hậu hành động",
            CheckStructuredVerifierToggleState);

        RunCheck(
            checks,
            "structured verifier xác minh ValuePattern bằng readback",
            CheckStructuredVerifierValueReadback);

        RunCheck(
            checks,
            "structured verifier coi Invoke transition là inconclusive thay vì fail mù",
            CheckStructuredVerifierInvokeIsInconclusive);

        RunCheck(
            checks,
            "structured invoke event relevance ưu tiên transition event",
            CheckStructuredInvokeEventRelevance);

        RunCheck(
            checks,
            "structured invoke local verify chỉ pass khi có transition quan sát được",
            CheckStructuredInvokeLocalVerificationRequiresTransition);

        RunCheck(
            checks,
            "structured target revalidation reject scene stale",
            CheckStructuredTargetRevalidationRejectsStaleScene);

        RunCheck(
            checks,
            "structured target revalidation remap token theo semantic duy nhất",
            CheckStructuredTargetRevalidationRemapsUniqueSemanticTarget);

        RunCheck(
            checks,
            "LegacyIAccessible có confidence thấp hơn UIA native",
            CheckStructuredCapabilityConfidenceDistinguishesLegacyFallback);


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
            "verification inconclusive đi qua diagnose trước replan",
            CheckActionStateMachineInconclusiveVerificationReplansSafely);



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
            "browser router ưu tiên Playwright khi DOM verify được",
            CheckBrowserRouterPrefersPlaywright);

        RunCheck(
            checks,
            "browser router fallback HTTP khi Playwright chưa verify",
            CheckBrowserRouterFallsBackToHttp);

        RunCheck(
            checks,
            "browser router chọn backend có confidence cao hơn",
            CheckBrowserRouterUsesHigherConfidenceBackend);

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
            "runtime capability thêm Playwright evidence cho browser",
            CheckUniversalRouterUsesPlaywrightCapability);

        RunCheck(
            checks,
            "runtime capability thêm FlaUI evidence cho computer",
            CheckUniversalRouterUsesFlaUiCapability);

        RunCheck(
            checks,
            "runtime capability không quảng bá adapter không khả dụng",
            CheckUniversalRouterIgnoresUnavailableCapability);

        RunCheck(
            checks,
            "adaptive wait hoàn thành khi có final evidence",
            CheckAdaptiveWaitCompletesOnVerifiedEvidence);

        RunCheck(
            checks,
            "adaptive wait không gia hạn vì visual animation yếu",
            CheckAdaptiveWaitIgnoresWeakVisualHeartbeat);

        RunCheck(
            checks,
            "adaptive wait stalled không bị coi là failed",
            CheckAdaptiveWaitStalledIsNotFailure);

        RunCheck(
            checks,
            "evidence fusion chấp nhận deterministic evidence mạnh",
            CheckEvidenceFusionAcceptsDeterministicEvidence);

        RunCheck(
            checks,
            "evidence fusion không tự kết luận khi nguồn mạnh xung đột",
            CheckEvidenceFusionRejectsStrongConflict);

        RunCheck(
            checks,
            "evidence fusion không double-count cùng source group",
            CheckEvidenceFusionDoesNotDoubleCountCorrelatedVisualEvidence);

        RunCheck(
            checks,
            "event-driven wake đánh thức verifier trước poll deadline",
            CheckEventDrivenWakeAcceleratesObservation);

        RunCheck(
            checks,
            "event wake không tự biến thành verification success",
            CheckEventWakeDoesNotCreateSuccess);

        RunCheck(
            checks,
            "event source không có tín hiệu vẫn polling fallback",
            CheckEventWakeFallsBackToPolling);

        RunCheck(
            checks,
            "event observation snapshot công bố counters an toàn",
            CheckEventObservationSnapshotContract);

        RunCheck(
            checks,
            "adaptive wait truyền đúng window target cho event wake",
            CheckAdaptiveWaitUsesTargetAwareWake);

        RunCheck(
            checks,
            "event relevance ưu tiên tín hiệu phù hợp với action",
            CheckEventRelevancePrioritizesExpectedSignals);

        RunCheck(
            checks,
            "event correlation dùng expected effect để tăng độ liên quan",
            CheckEventRelevanceUsesExpectedEffect);

        RunCheck(
            checks,
            "event source fusion tăng confidence khi WinEvent và UIA3 xác nhận nhau",
            CheckEventSourceFusionConfidence);

        RunCheck(
            checks,
            "event burst gom tín hiệu dồn dập thành một transaction UI",
            CheckEventBurstGrouping);

        RunCheck(
            checks,
            "unified desktop state mang theo structured UI scene",
            CheckUnifiedDesktopStateIncludesStructuredScene);

        RunCheck(
            checks,
            "structured scene graph giữ parent-child và capability tương tác",
            CheckStructuredSceneGraphBuildsHierarchy);

        RunCheck(
            checks,
            "structured resolver chọn duy nhất target đủ chắc chắn",
            CheckStructuredResolverChoosesUniqueTarget);

        RunCheck(
            checks,
            "structured resolver từ chối target mơ hồ",
            CheckStructuredResolverRejectsAmbiguousTarget);








        RunCheck(
            checks,
            "capability cache hit không gọi raw discovery lần hai",
            CheckCapabilityCacheAvoidsRepeatedDiscovery);

        RunCheck(
            checks,
            "capability cache invalidate buộc refresh",
            CheckCapabilityCacheInvalidationForcesRefresh);

        RunCheck(
            checks,
            "capability health override tạm ngừng adapter lỗi",
            CheckCapabilityCacheHealthOverride);

        RunCheck(
            checks,
            "resilience không retry permission denial",
            CheckResilienceStopsOnPermissionDenial);

        RunCheck(
            checks,
            "resilience yêu cầu verify trước retry khi side effect có thể đã xảy ra",
            CheckResilienceRequiresVerificationAfterPossibleSideEffect);

        RunCheck(
            checks,
            "resilience chỉ retry lỗi kỹ thuật read-only",
            CheckResilienceRetriesTransientReadOnlyFailure);

        RunCheck(
            checks,
            "checkpoint restart chuyển running thành interrupted và giữ mốc đã xác minh",
            CheckOperatorCheckpointRecoversInterruptedRun);

        RunCheck(
            checks,
            "checkpoint bị cô lập theo workspace",
            CheckOperatorCheckpointWorkspaceIsolation);

        RunCheck(
            checks,
            "checkpoint completed không được resume",
            CheckOperatorCheckpointCompletedIsNotResumable);

        RunCheck(
            checks,
            "checkpoint không lưu plaintext nhạy cảm",
            CheckOperatorCheckpointRedactsSensitiveContext);

        RunCheck(
            checks,
            "telemetry không công bố payload nhạy cảm",
            CheckOperatorTelemetryDoesNotExposePayloads);

        RunCheck(
            checks,
            "telemetry đổi dimension không an toàn thành other",
            CheckOperatorTelemetrySanitizesUnsafeDimensions);

        RunCheck(
            checks,
            "telemetry giới hạn danh sách sự kiện gần nhất",
            CheckOperatorTelemetryBoundsRecentEvents);

        RunCheck(
            checks,
            "telemetry aggregate phân biệt structured-first và Gemini route",
            CheckOperatorTelemetryAggregatesRoutes);

        RunCheck(
            checks,
            "forensic cycle summary giữ đủ trường chẩn đoán cốt lõi",
            CheckForensicCycleSummaryCoreFields);

        RunCheck(
            checks,
            "forensic window delta ghi appeared disappeared và moved",
            CheckForensicWindowDelta);

        RunCheck(
            checks,
            "attachment hybrid router chọn direct hay RAG đúng loại tệp",
            CheckAttachmentHybridRouting);

        RunCheck(
            checks,
            "reliable operator runtime chỉ ready khi có desktop capability",
            CheckReliableOperatorRuntimeReadiness);

        RunCheck(
            checks,
            "reliable operator runtime chặn khi không có desktop capability",
            CheckReliableOperatorRuntimeNotReady);

        RunCheck(
            checks,
            "reliable operator chấp nhận verifier cửa sổ deterministic",
            CheckReliableOperatorAcceptsDeterministicWindowVerification);

        RunCheck(
            checks,
            "reliable operator yêu cầu semantic cho strong-local đơn lẻ",
            CheckReliableOperatorRequiresSemanticForStrongLocal);

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
            "agent outcome verification dùng universal evidence adapter",
            CheckAgentOutcomeVerificationUsesUniversalEvidenceAdapter);

        RunCheck(
            checks,
            "agent outcome chưa verified vẫn bắt buộc verify tiếp",
            CheckAgentOutcomeVerificationRequiresEvidence);

        RunCheck(
            checks,
            "agent contract không hỗ trợ verification không được complete",
            CheckAgentOutcomeVerificationRejectsUnsupportedContract);

        RunCheck(
            checks,
            "execution lifecycle chỉ complete sau verification",
            CheckExecutionLifecycleCompletesOnlyAfterVerification);

        RunCheck(
            checks,
            "execution lifecycle thiếu evidence không được complete",
            CheckExecutionLifecycleBlocksMissingEvidence);

        RunCheck(
            checks,
            "execution lifecycle dừng khi agent execution fail",
            CheckExecutionLifecycleStopsOnExecutionFailure);

        RunCheck(
            checks,
            "verification continuation browser dùng structured readback",
            CheckVerificationContinuationBrowserReadback);

        RunCheck(
            checks,
            "verification continuation computer dùng observe verify",
            CheckVerificationContinuationComputerObserveVerify);

        RunCheck(
            checks,
            "verified outcome không tạo continuation verification thừa",
            CheckVerificationContinuationSkipsVerifiedOutcome);

        RunCheck(
            checks,
            "safe browser continuation có thể complete sau structured readback",
            CheckSafeBrowserContinuationCompletesAfterReadback);

        RunCheck(
            checks,
            "safe browser continuation sai URL buộc replan",
            CheckSafeBrowserContinuationMismatchReplans);

        RunCheck(
            checks,
            "coding continuation thiếu context giữ pending không replay",
            CheckSafeCodingContinuationStaysPending);

        RunCheck(
            checks,
            "coding verification context pass đưa lifecycle tới complete",
            CheckCodingVerificationContextCompletesLifecycle);

        RunCheck(
            checks,
            "coding verification context fail buộc lifecycle replan",
            CheckCodingVerificationContextFailureReplans);

        RunCheck(
            checks,
            "generic text engine direct set và local verify",
            CheckGenericTextEngineDirectSetAndVerify);

        RunCheck(
            checks,
            "generic text engine repair tối đa một lần",
            CheckGenericTextEngineRepairsOnce);

        RunCheck(
            checks,
            "generic text engine chặn trường nhạy cảm",
            CheckGenericTextEngineRejectsSensitiveTarget);

        RunCheck(
            checks,
            "FlaUI được ưu tiên khi có bằng chứng cấu trúc",
            CheckFlaUiCompositePrefersStructuredBackend);

        RunCheck(
            checks,
            "FlaUI thiếu capability thì fallback Win32",
            CheckFlaUiCompositeFallsBackToWin32);

        RunCheck(
            checks,
            "FlaUI phát hiện trường nhạy cảm thì không fallback",
            CheckFlaUiCompositePreservesSensitiveDetection);

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

    private static void CheckCaptureBackendRouterOrder()
    {
        var router = new DesktopCaptureBackendRouter();
        var candidates = router.GetOrderedCandidates(
            DesktopCaptureScopes.Window);

        Require(
            candidates.SequenceEqual(
                [
                    DesktopCaptureBackends.WindowsGraphicsCapture,
                    DesktopCaptureBackends.PrintWindow,
                    DesktopCaptureBackends.CopyFromScreen
                ]),
            "Capture router không giữ đúng thứ tự ưu tiên backend cho window.");

        var monitorCandidates = router.GetOrderedCandidates(
            DesktopCaptureScopes.Monitor);

        Require(
            monitorCandidates.SequenceEqual(
                [
                    DesktopCaptureBackends.WindowsGraphicsCapture,
                    DesktopCaptureBackends.DxgiDesktopDuplication,
                    DesktopCaptureBackends.CopyFromScreen
                ]),
            "Capture router không giữ đúng thứ tự ưu tiên WGC → DXGI → CopyFromScreen cho monitor.");

        var virtualCandidates =
            router.GetOrderedCandidates(
                DesktopCaptureScopes.VirtualDesktop);

        Require(
            virtualCandidates.SequenceEqual(
                [
                    DesktopCaptureBackends.DxgiDesktopDuplication,
                    DesktopCaptureBackends.CopyFromScreen
                ]),
            "Capture router không giữ đúng thứ tự ưu tiên DXGI → CopyFromScreen cho virtual desktop.");
    }

    private static void CheckCaptureBackendRouterAvailableFallback()
    {
        var router = new DesktopCaptureBackendRouter();

        Require(
            router.GetPreferredAvailableBackend(
                DesktopCaptureScopes.Window) ==
            DesktopCaptureBackends.PrintWindow,
            "Window capture chưa được chọn PrintWindow khi WGC chưa kích hoạt.");

        Require(
            router.GetPreferredAvailableBackend(
                DesktopCaptureScopes.Monitor) ==
            DesktopCaptureBackends.CopyFromScreen,
            "Monitor capture chưa fallback CopyFromScreen khi WGC/DXGI chưa kích hoạt.");

        Require(
            router.GetPreferredAvailableBackend(
                DesktopCaptureScopes.VirtualDesktop) ==
            DesktopCaptureBackends.CopyFromScreen,
            "Virtual desktop chưa fallback CopyFromScreen khi DXGI chưa kích hoạt.");
    }

    private static void CheckCaptureHealthDetectsRepeatedFrames()
    {
        var tracker =
            new DesktopCaptureHealthTracker();

        var signature =
            new byte[] { 10, 20, 30, 40 };

        for (var index = 0; index < 5; index++)
        {
            tracker.RecordSuccess(
                DesktopCaptureScopes.Monitor,
                @"\\.\DISPLAY1",
                DesktopCaptureBackends.WindowsGraphicsCapture,
                10 + index,
                signature,
                index == 0
                    ? null
                    : "acceptance fallback marker");
        }

        var snapshot =
            tracker.GetSnapshot();

        var entry =
            snapshot.Entries.Single();

        Require(
            entry.SuccessCount == 5 &&
            entry.ConsecutiveUnchangedFrames == 4 &&
            entry.StaleSuspected &&
            entry.AverageLatencyMs > 0 &&
            entry.FallbackCount == 4,
            "Capture health chưa phát hiện đúng chuỗi frame lặp hoặc telemetry latency/fallback.");

        tracker.Reset();

        Require(
            tracker.GetSnapshot().Entries.Count == 0,
            "Capture health reset chưa xóa telemetry trong bộ nhớ.");
    }

    private static void CheckCaptureRouterDeprioritizesStaleBackend()
    {
        var health =
            new DesktopCaptureHealthTracker();

        var signature =
            new byte[] { 1, 2, 3, 4 };

        for (var index = 0; index < 5; index++)
        {
            health.RecordSuccess(
                DesktopCaptureScopes.Monitor,
                @"\\.\DISPLAY1",
                DesktopCaptureBackends.WindowsGraphicsCapture,
                20,
                signature);
        }

        var router =
            new DesktopCaptureBackendRouter(
                wgc: null,
                dxgi: null,
                health);

        var candidates =
            router.GetOrderedCandidates(
                DesktopCaptureScopes.Monitor,
                @"\\.\DISPLAY1");

        Require(
            candidates.SequenceEqual(
                [
                    DesktopCaptureBackends.DxgiDesktopDuplication,
                    DesktopCaptureBackends.CopyFromScreen,
                    DesktopCaptureBackends.WindowsGraphicsCapture
                ]),
            "Capture router chưa hạ ưu tiên WGC khi backend bị stale trên đúng monitor target.");

        var otherMonitor =
            router.GetOrderedCandidates(
                DesktopCaptureScopes.Monitor,
                @"\\.\DISPLAY2");

        Require(
            otherMonitor.First() ==
            DesktopCaptureBackends.WindowsGraphicsCapture,
            "Health penalty của một monitor đã làm ảnh hưởng target monitor khác.");
    }

    private static void CheckWindowsOcrPayloadParsing()
    {
        const string json = """
        {
          "text": "Save File",
          "language": "en-US",
          "lines": [
            {
              "text": "Save File",
              "words": [
                {
                  "text": "Save",
                  "left": 12,
                  "top": 20,
                  "width": 40,
                  "height": 18
                },
                {
                  "text": "File",
                  "left": 58,
                  "top": 20,
                  "width": 30,
                  "height": 18
                }
              ]
            }
          ]
        }
        """;

        var result =
            WindowsDesktopOcrSensor.ParseForAcceptance(
                json);

        Require(
            result.Available &&
            result.Text == "Save File" &&
            result.Language == "en-US" &&
            result.Lines.Count == 1 &&
            result.WordCount == 2 &&
            result.Lines[0].Words[0].Text == "Save" &&
            Math.Abs(
                result.Lines[0].Words[0].Left -
                12) < 0.001,
            "Windows OCR parser chưa giữ đúng text/language/bounding boxes.");
    }

    private static void CheckOcrResolverChoosesUniqueTarget()
    {
        var resolver =
            new DesktopOcrTargetResolver();

        var observation =
            new DesktopOcrObservation(
                Available: true,
                Text: "Cancel Continue",
                Language: "en-US",
                Lines:
                [
                    new DesktopOcrLine(
                        "Cancel Continue",
                        [
                            new DesktopOcrWord(
                                "Cancel",
                                10,
                                20,
                                50,
                                20),
                            new DesktopOcrWord(
                                "Continue",
                                120,
                                20,
                                80,
                                20)
                        ])
                ],
                CaptureWidth: 800,
                CaptureHeight: 600,
                Provider: "acceptance",
                Reason: "acceptance");

        var result =
            resolver.Resolve(
                observation,
                "Continue");

        Require(
            result.Resolved &&
            result.Target is not null &&
            result.Target.Text == "Continue" &&
            result.Target.Score >= 110 &&
            Math.Abs(
                result.Target.Left -
                120) < 0.001,
            "OCR resolver chưa chọn đúng target text duy nhất.");
    }

    private static void CheckOcrResolverRejectsAmbiguousTarget()
    {
        var resolver =
            new DesktopOcrTargetResolver();

        var observation =
            new DesktopOcrObservation(
                Available: true,
                Text: "Open Open",
                Language: "en-US",
                Lines:
                [
                    new DesktopOcrLine(
                        "Open",
                        [
                            new DesktopOcrWord(
                                "Open",
                                10,
                                20,
                                50,
                                20)
                        ]),
                    new DesktopOcrLine(
                        "Open",
                        [
                            new DesktopOcrWord(
                                "Open",
                                200,
                                20,
                                50,
                                20)
                        ])
                ],
                CaptureWidth: 800,
                CaptureHeight: 600,
                Provider: "acceptance",
                Reason: "acceptance");

        var result =
            resolver.Resolve(
                observation,
                "Open");

        Require(
            result.Status ==
                DesktopOcrResolutionStatus.Ambiguous &&
            !result.Resolved &&
            result.Target is null,
            "OCR resolver đang đoán khi có nhiều target cùng độ chắc chắn.");
    }

    private static void CheckOcrPlannerMapsCaptureBoxToFrame()
    {
        var observation =
            new DesktopOcrObservation(
                Available: true,
                Text: "Continue",
                Language: "en-US",
                Lines:
                [
                    new DesktopOcrLine(
                        "Continue",
                        [
                            new DesktopOcrWord(
                                "Continue",
                                100,
                                50,
                                50,
                                20)
                        ])
                ],
                CaptureWidth: 400,
                CaptureHeight: 200,
                Provider: "acceptance",
                Reason: "acceptance");

        var planner =
            new DesktopOcrActionPlanner(
                new AcceptanceOcrSensor(
                    observation),
                new LocalVisualTargetResolver(),
                new LocalVisualSensorBudgetPolicy());

        var foreground =
            new ComputerWindowInfo(
                "0x1234",
                "Acceptance",
                "acceptance",
                10,
                true,
                100,
                40,
                800,
                400);

        var state =
            new ComputerOperatorDesktopState(
                DateTimeOffset.UtcNow,
                foreground,
                [foreground],
                FrameLeft: 0,
                FrameTop: 0,
                FrameWidth: 1200,
                FrameHeight: 800,
                CaptureScope:
                    DesktopCaptureScopes.VirtualDesktop,
                CaptureWindowId: string.Empty,
                CaptureWindowWasForeground: true);

        var planned =
            planner.TryPlan(
                "Bấm chữ Continue",
                state,
                out var decision);

        Require(
            planned &&
            decision.Action == "click-left" &&
            decision.BoxLeft == 300 &&
            decision.BoxTop == 140 &&
            decision.BoxWidth == 100 &&
            decision.BoxHeight == 40 &&
            decision.ImageX == 350 &&
            decision.ImageY == 160,
            $"OCR planner map sai capture→frame: box=({decision.BoxLeft},{decision.BoxTop},{decision.BoxWidth},{decision.BoxHeight}), point=({decision.ImageX},{decision.ImageY}).");
    }

    private static void CheckOcrProviderRouterFallbackOrder()
    {
        var windows =
            new AcceptanceOcrProvider(
                "windows-ocr",
                new DesktopOcrObservation(
                    Available: false,
                    Text: string.Empty,
                    Language: string.Empty,
                    Lines:
                        Array.Empty<DesktopOcrLine>(),
                    CaptureWidth: 0,
                    CaptureHeight: 0,
                    Provider: "windows-ocr",
                    Reason: "unavailable"));

        var paddle =
            new AcceptanceOcrProvider(
                "paddleocr-onnx",
                new DesktopOcrObservation(
                    Available: true,
                    Text: "Continue",
                    Language: "en",
                    Lines:
                    [
                        new DesktopOcrLine(
                            "Continue",
                            [
                                new DesktopOcrWord(
                                    "Continue",
                                    10,
                                    10,
                                    60,
                                    20)
                            ])
                    ],
                    CaptureWidth: 800,
                    CaptureHeight: 600,
                    Provider: "paddleocr-onnx",
                    Reason: "acceptance"));

        var router =
            new DesktopOcrSensorRouter(
                [paddle, windows],
                new LocalVisualProviderHealthRegistry(),
                new LocalVisualSensorBudgetPolicy());

        var result =
            router.ReadWindow(
                "0x1234");

        Require(
            windows.CallCount == 1 &&
            paddle.CallCount == 1 &&
            result.Available &&
            result.Provider == "paddleocr-onnx" &&
            result.Text == "Continue",
            "OCR provider router chưa ưu tiên Windows hoặc chưa fallback đúng sang PaddleOCR/ONNX.");
    }

    private static void CheckPaddleWorkerInvocationFailureIsIsolated()
    {
        var result =
            LocalVisionWorkerClient.InvokeProcessForAcceptance(
                executable:
                    "__personal_ai_missing_worker__",
                arguments:
                    string.Empty,
                request:
                    "{}",
                timeoutMilliseconds:
                    250);

        Require(
            !result.Success &&
            !result.TimedOut &&
            !string.IsNullOrWhiteSpace(
                result.Detail),
            "PaddleOCR worker launch failure chưa được cô lập thành kết quả an toàn.");
    }

    private static void CheckLocalVisionWorkerTimeoutBounds()
    {
        Require(
            LocalVisionWorkerClient.NormalizeTimeoutForAcceptance(
                1) == 250 &&
            LocalVisionWorkerClient.NormalizeTimeoutForAcceptance(
                4500) == 4500 &&
            LocalVisionWorkerClient.NormalizeTimeoutForAcceptance(
                99999) == 15000,
            "Local vision worker timeout chưa được clamp trong biên 250..15000ms.");
    }

    private static void CheckLocalVisionWorkerRecyclePolicy()
    {
        Require(
            !LocalVisionWorkerClient.ShouldRecycleForAcceptance(
                requestCount: 8,
                age: TimeSpan.FromMinutes(2),
                idle: TimeSpan.FromSeconds(10)) &&
            LocalVisionWorkerClient.ShouldRecycleForAcceptance(
                requestCount: 128,
                age: TimeSpan.FromMinutes(2),
                idle: TimeSpan.FromSeconds(10)) &&
            LocalVisionWorkerClient.ShouldRecycleForAcceptance(
                requestCount: 8,
                age: TimeSpan.FromMinutes(30),
                idle: TimeSpan.FromSeconds(10)) &&
            LocalVisionWorkerClient.ShouldRecycleForAcceptance(
                requestCount: 8,
                age: TimeSpan.FromMinutes(2),
                idle: TimeSpan.FromMinutes(2)),
            "Local vision warm worker recycle policy chưa khóa đúng max-request/max-age/idle.");
    }

    private static void CheckProviderCooldownDelayBounds()
    {
        var now =
            DateTimeOffset.UtcNow;

        Require(
            ComputerOperatorTaskService.CalculateProviderCooldownDelayMillisecondsForAcceptance(
                now.AddMilliseconds(100),
                now) == 900 &&
            ComputerOperatorTaskService.CalculateProviderCooldownDelayMillisecondsForAcceptance(
                now.AddMilliseconds(2500),
                now) == 2500 &&
            ComputerOperatorTaskService.CalculateProviderCooldownDelayMillisecondsForAcceptance(
                now.AddSeconds(12),
                now) == 5000,
            "Provider cooldown delay chưa clamp đúng trong biên 900..5000ms.");
    }

    private static void CheckGeminiOperatorStructuredSchema()
    {
        var schema =
            DesktopVisionService.CreateDesktopOperatorResponseJsonSchemaForAcceptance();

        Require(
            schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("properties", out var properties) &&
            properties.TryGetProperty("action", out var action) &&
            action.TryGetProperty("enum", out var actions) &&
            actions.EnumerateArray().Any(item =>
                item.GetString() == "wait") &&
            properties.TryGetProperty("sceneElements", out var sceneElements) &&
            sceneElements.TryGetProperty("maxItems", out var maxItems) &&
            maxItems.GetInt32() == 12,
            "Gemini operator schema chưa khóa action enum hoặc giới hạn sceneElements đúng thiết kế.");
    }

    private static void CheckMinimalIntentSchema()
    {
        var schema =
            DesktopVisionService.CreateDesktopOperatorIntentJsonSchemaForAcceptance();

        Require(
            schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("properties", out var properties) &&
            properties.TryGetProperty("intent", out var intent) &&
            intent.TryGetProperty("enum", out var intents) &&
            intents.EnumerateArray().Any(item =>
                item.GetString() == "click_target") &&
            !properties.TryGetProperty("sceneElements", out _) &&
            !properties.TryGetProperty("plan", out _) &&
            !properties.TryGetProperty("verifiedMilestones", out _),
            "Minimal Intent schema vẫn mang payload planner lớn hoặc thiếu intent enum.");
    }

    private static void CheckUnifiedDecisionKernelCompilesSafeAction()
    {
        var kernel =
            new UnifiedDecisionKernel();

        var frame =
            new DesktopScreenshotFrame(
                Jpeg: Array.Empty<byte>(),
                Left: 0,
                Top: 0,
                Width: 1920,
                Height: 1080,
                CapturedAtUtc: DateTimeOffset.UtcNow);

        var intent =
            new DesktopOperatorIntent(
                Intent: "click_target",
                Target: "Phòng Tập",
                Query: string.Empty,
                Text: string.Empty,
                Key: string.Empty,
                Keys: Array.Empty<string>(),
                ImageX: 500,
                ImageY: 300,
                BoxLeft: 450,
                BoxTop: 270,
                BoxWidth: 120,
                BoxHeight: 60,
                ScrollDelta: 0,
                ExpectedEffect: "Màn hình Phòng Tập được mở.",
                Confidence: 0.94,
                Reason: "Nút Phòng Tập đang hiển thị rõ.");

        var compiled =
            kernel.TryCompile(
                intent,
                frame,
                out var decision,
                out _);

        var unsafeIntent =
            intent with
            {
                ImageX = 900,
                ImageY = 700
            };

        var unsafeCompiled =
            kernel.TryCompile(
                unsafeIntent,
                frame,
                out _,
                out _);

        Require(
            compiled &&
            decision.Action == "click-left" &&
            decision.TargetLabel == "Phòng Tập" &&
            decision.BoxWidth == 120 &&
            decision.ExpectedEffect.Contains(
                "Phòng Tập",
                StringComparison.OrdinalIgnoreCase) &&
            !unsafeCompiled,
            "Unified Decision Kernel chưa compile/reject intent pointer đúng policy an toàn.");
    }

    private static void CheckUnifiedDecisionKernelRejectsUnsafeSideEffect()
    {
        var kernel =
            new UnifiedDecisionKernel();

        var frame =
            new DesktopScreenshotFrame(
                Jpeg: Array.Empty<byte>(),
                Left: 0,
                Top: 0,
                Width: 1280,
                Height: 720,
                CapturedAtUtc: DateTimeOffset.UtcNow);

        var intent =
            new DesktopOperatorIntent(
                Intent: "press_key",
                Target: "Xác nhận",
                Query: string.Empty,
                Text: string.Empty,
                Key: "ENTER",
                Keys: Array.Empty<string>(),
                ImageX: 0,
                ImageY: 0,
                BoxLeft: 0,
                BoxTop: 0,
                BoxWidth: 0,
                BoxHeight: 0,
                ScrollDelta: 0,
                ExpectedEffect: string.Empty,
                Confidence: 0.91,
                Reason: "Cần xác nhận bước hiện tại.");

        var compiled =
            kernel.TryCompile(
                intent,
                frame,
                out _,
                out var rejection);

        Require(
            !compiled &&
            rejection.Contains(
                "expectedEffect",
                StringComparison.OrdinalIgnoreCase),
            "Unified Decision Kernel chưa chặn side-effect thiếu expectedEffect.");
    }

    private static void CheckVisualSceneIdentityIncludesPerceptualHash()
    {
        var foreground =
            new ComputerWindowInfo(
                "0x1234",
                "Acceptance App",
                "acceptance",
                10,
                true,
                0,
                0,
                1280,
                720);

        var state =
            new ComputerOperatorDesktopState(
                DateTimeOffset.UtcNow,
                foreground,
                [foreground],
                FrameLeft: 0,
                FrameTop: 0,
                FrameWidth: 1280,
                FrameHeight: 720,
                CaptureScope: DesktopCaptureScopes.VirtualDesktop,
                CaptureWindowId: null,
                CaptureWindowWasForeground: false);

        var first =
            ComputerOperatorSceneIdentityService.BuildFingerprintForAcceptance(
                state,
                new DesktopPerceptualHash(
                    0x0000000000000000UL,
                    "dhash-9x8"));

        var second =
            ComputerOperatorSceneIdentityService.BuildFingerprintForAcceptance(
                state,
                new DesktopPerceptualHash(
                    0xFFFFFFFFFFFFFFFFUL,
                    "dhash-9x8"));

        Require(
            !first.Equals(
                second,
                StringComparison.Ordinal),
            "Visual scene identity chưa đổi khi perceptual hash thay đổi mạnh.");
    }


    private static void CheckDesktopSceneFingerprintContract()
    {
        var foreground =
            new ComputerWindowInfo(
                "0x5678",
                "Fingerprint Contract",
                "acceptance",
                20,
                true,
                0,
                0,
                1920,
                1080);

        var state =
            new ComputerOperatorDesktopState(
                DateTimeOffset.UtcNow,
                foreground,
                [foreground],
                FrameLeft: 0,
                FrameTop: 0,
                FrameWidth: 1920,
                FrameHeight: 1080,
                CaptureScope: DesktopCaptureScopes.VirtualDesktop,
                CaptureWindowId: null,
                CaptureWindowWasForeground: false);

        var fingerprint =
            ComputerOperatorSceneIdentityService.BuildFingerprintForAcceptance(
                state);

        Require(
            fingerprint.Length == 64 &&
            fingerprint.All(character =>
                character is >= '0' and <= '9' ||
                character is >= 'A' and <= 'F'),
            "BuildDesktopSceneFingerprint phải trả SHA-256 hex 64 ký tự, không được trả raw scene material.");
    }


    private static void CheckUnifiedVerificationEngineAuthority()
    {
        Require(
            typeof(IComputerOperatorVerificationEngine)
                .IsAssignableFrom(
                    typeof(ComputerOperatorVerificationEngine)) &&
            ComputerOperatorVerificationStatuses.Verified == "verified" &&
            ComputerOperatorVerificationStatuses.Failed == "failed" &&
            ComputerOperatorVerificationStatuses.Wait == "wait" &&
            ComputerOperatorVerificationStatuses.SemanticRequired == "semantic-required",
            "Unified Verification Engine phải là authority duy nhất và có đúng bốn trạng thái chuẩn.");
    }

    private static void CheckVerificationConflictRequiresReobserve()
    {
        Require(
            ComputerOperatorVerificationEngine.ShouldReobserveOnSemanticConflictForAcceptance(
                semanticSatisfied: false,
                strongLocalVisualTransition: true) &&
            !ComputerOperatorVerificationEngine.ShouldReobserveOnSemanticConflictForAcceptance(
                semanticSatisfied: true,
                strongLocalVisualTransition: true) &&
            !ComputerOperatorVerificationEngine.ShouldReobserveOnSemanticConflictForAcceptance(
                semanticSatisfied: false,
                strongLocalVisualTransition: false),
            "Verification conflict policy chưa phân biệt đúng local transition mạnh và semantic false.");
    }

    private static void CheckVisionProviderExhaustionIsRecoverable()
    {
        Require(
            ComputerOperatorTaskService.IsVisionProviderExhaustedForAcceptance(
                new InvalidOperationException(
                    "Tất cả Computer Operator vision provider đều thất bại cho 'verify'. Đã thử: Gemini, OpenAI.")) &&
            ComputerOperatorTaskService.IsVisionProviderExhaustedForAcceptance(
                new InvalidOperationException(
                    "Chưa có Computer Operator vision provider nào được cấu hình.")) &&
            !ComputerOperatorTaskService.IsVisionProviderExhaustedForAcceptance(
                new InvalidOperationException(
                    "Lỗi state machine không liên quan provider.")),
            "Provider exhaustion phải được nhận diện riêng để verification chuyển sang inconclusive/reobserve thay vì làm chết toàn bộ task.");
    }

    private static void CheckPostTransitionSuppression()
    {
        const string signature =
            "click-left|label=nút chơi|effect=menu chọn chế độ chơi xuất hiện";

        Require(
            ComputerOperatorTaskService.ShouldSuppressRecentlyConsumedActionForAcceptance(
                signature,
                signature,
                currentStep: 11,
                consumedUntilStep: 14) &&
            ComputerOperatorTaskService.ShouldDeferLoopAssessmentForConsumedActionForAcceptance(
                signature,
                signature,
                currentStep: 12,
                consumedUntilStep: 14) &&
            !ComputerOperatorTaskService.ShouldSuppressRecentlyConsumedActionForAcceptance(
                signature,
                signature,
                currentStep: 15,
                consumedUntilStep: 14) &&
            !ComputerOperatorTaskService.ShouldDeferLoopAssessmentForConsumedActionForAcceptance(
                "click-left|label=phòng tập|effect=mở phòng tập",
                signature,
                currentStep: 12,
                consumedUntilStep: 14),
            "Post-transition suppression phải chặn replay trước Loop Guard trong protection window nhưng không chặn strategy mới.");
    }

    private static void CheckSingleDecisionAuthority()
    {
        var authority =
            new ComputerOperatorDecisionAuthority();

        var noLoop =
            new ComputerOperatorLoopAssessment(
                false,
                false,
                string.Empty,
                string.Empty,
                0);

        var execute =
            authority.Evaluate(
                new ComputerOperatorDecisionAuthorityInput(
                    "click-left",
                    "click-left|label=sample",
                    RecentlyConsumedReplay: false,
                    noLoop,
                    RecoveryRecommendation: null));

        var consumed =
            authority.Evaluate(
                new ComputerOperatorDecisionAuthorityInput(
                    "click-left",
                    "click-left|label=sample",
                    RecentlyConsumedReplay: true,
                    noLoop,
                    RecoveryRecommendation: null));

        var loop =
            new ComputerOperatorLoopAssessment(
                true,
                true,
                "repeated-strategy",
                "strategy lặp",
                3);

        var recommendation =
            new ComputerOperatorRecoveryCoordinatorDecision(
                ComputerOperatorRecoveryCoordinatorDecisions.ResumeFragment,
                AllowSameStrategyRetry: false,
                RequiresFreshObservation: true,
                PreferKnownFragment: true,
                "Ưu tiên fragment đã verify.");

        var replan =
            authority.Evaluate(
                new ComputerOperatorDecisionAuthorityInput(
                    "click-left",
                    "click-left|label=sample",
                    RecentlyConsumedReplay: false,
                    loop,
                    recommendation));

        Require(
            execute.AllowExecution &&
            execute.Directive ==
                ComputerOperatorDecisionAuthorityDirectives.Execute &&
            !consumed.AllowExecution &&
            consumed.Directive ==
                ComputerOperatorDecisionAuthorityDirectives.Replan &&
            !replan.AllowExecution &&
            replan.PreferKnownFragment &&
            replan.Directive ==
                ComputerOperatorDecisionAuthorityDirectives.Replan,
            "Decision Authority chưa gom đúng quyền execute/replan từ planner, consumed transition và loop/recovery signals.");
    }

    private static void CheckDeterministicTargetGroundingLabels()
    {
        var labels =
            DesktopOcrActionPlanner.BuildGroundingLabels(
                "League of Legends application icon in search results");

        var shortAppLabels =
            DesktopOcrActionPlanner.BuildGroundingLabels(
                "Sample Editor App");

        Require(
            labels.Contains(
                "League of Legends",
                StringComparer.OrdinalIgnoreCase) &&
            shortAppLabels.Contains(
                "Sample Editor",
                StringComparer.OrdinalIgnoreCase),
            "Semantic target label phải bỏ cả context mô tả dài lẫn hậu tố app/application chung trước local OCR grounding.");
    }

    private static void CheckWindowsSearchUsesDeterministicGrounding()
    {
        var search =
            new ComputerWindowInfo(
                "0x100",
                "Search",
                "SearchHost",
                10,
                true,
                0,
                0,
                1200,
                900);

        var browser =
            new ComputerWindowInfo(
                "0x200",
                "Search",
                "msedge",
                11,
                true,
                0,
                0,
                1200,
                900);

        Require(
            ComputerOperatorTaskService.IsDeterministicTextSurfaceForAcceptance(
                search) &&
            !ComputerOperatorTaskService.IsDeterministicTextSurfaceForAcceptance(
                browser),
            "Deterministic text surface policy chưa phân biệt đúng Windows Search và browser.");
    }

    private static void CheckUnifiedGroundingPlannerFallback()
    {
        var decision =
            new DesktopOperatorDecision(
                State: "sample",
                Plan: "sample",
                CurrentSubgoal: "sample",
                GoalProgress: 0,
                VerifiedMilestones: Array.Empty<string>(),
                Action: "click-left",
                Query: string.Empty,
                Text: string.Empty,
                Key: string.Empty,
                Keys: Array.Empty<string>(),
                Url: string.Empty,
                TargetLabel: "Sample target",
                CoordinateSpace: ComputerCoordinateSpaces.ImagePixel,
                CoordinateWindowId: "0x1",
                ImageX: 150,
                ImageY: 120,
                EndImageX: 0,
                EndImageY: 0,
                NormalizedX: 0,
                NormalizedY: 0,
                EndNormalizedX: 0,
                EndNormalizedY: 0,
                BoxLeft: 100,
                BoxTop: 90,
                BoxWidth: 100,
                BoxHeight: 60,
                BoxNormalizedLeft: 0,
                BoxNormalizedTop: 0,
                BoxNormalizedWidth: 0,
                BoxNormalizedHeight: 0,
                ScrollDelta: 0,
                ExpectedEffect: "sample effect",
                Confidence: 0.92,
                Reason: "planner visual",
                SceneElements: Array.Empty<DesktopSceneElement>(),
                TargetElementId: string.Empty);

        Require(
            ComputerOperatorGroundingService.CanUsePlannerVisualFallbackForAcceptance(
                decision,
                deterministicTextSurface: false) &&
            !ComputerOperatorGroundingService.CanUsePlannerVisualFallbackForAcceptance(
                decision,
                deterministicTextSurface: true) &&
            !ComputerOperatorGroundingService.CanUsePlannerVisualFallbackForAcceptance(
                decision with { Confidence = 0.60 },
                deterministicTextSurface: false),
            "Unified Grounding Contract phải cho phép nguồn planner visual độc lập khi đủ mạnh nhưng vẫn fail-closed trên deterministic text surface hoặc confidence thấp.");
    }

    private static void CheckNativeAppLaunchRejectsBrowserIdentity()
    {
        var decision =
            new DesktopOperatorDecision(
                State: string.Empty,
                Plan: string.Empty,
                CurrentSubgoal: "Mở ứng dụng League of Legends.",
                GoalProgress: 0,
                VerifiedMilestones: Array.Empty<string>(),
                Action: "click-left",
                Query: string.Empty,
                Text: string.Empty,
                Key: string.Empty,
                Keys: Array.Empty<string>(),
                Url: string.Empty,
                TargetLabel: "League of Legends",
                CoordinateSpace: ComputerCoordinateSpaces.ImagePixel,
                CoordinateWindowId: string.Empty,
                ImageX: 100,
                ImageY: 100,
                EndImageX: 0,
                EndImageY: 0,
                NormalizedX: 0,
                NormalizedY: 0,
                EndNormalizedX: 0,
                EndNormalizedY: 0,
                BoxLeft: 50,
                BoxTop: 50,
                BoxWidth: 100,
                BoxHeight: 50,
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

        Require(
            DesktopVerificationRouter.IsBrowserLaunchMismatchForAcceptance(
                decision,
                "msedge") &&
            !DesktopVerificationRouter.IsBrowserLaunchMismatchForAcceptance(
                decision,
                "RiotClientServices"),
            "Process-aware app launch verifier chưa chặn browser mismatch đúng cách.");
    }

    private static void CheckLocalVisualProviderHealthCooldown()
    {
        var health =
            new LocalVisualProviderHealthRegistry();

        for (var i = 0; i < 3; i++)
        {
            health.RecordFailure(
                "windows-ocr",
                100,
                "acceptance failure");
        }

        var skip =
            health.ShouldSkip(
                "windows-ocr",
                DateTimeOffset.UtcNow,
                out var reason);

        var snapshot =
            health.Get(
                "windows-ocr");

        Require(
            skip &&
            snapshot.State ==
                LocalVisualProviderHealthState.Unavailable &&
            snapshot.ConsecutiveFailures == 3 &&
            !string.IsNullOrWhiteSpace(reason),
            "Local visual provider health chưa cooldown sau 3 failure liên tiếp.");
    }

    private static void CheckLocalVisualProviderHealthResetsOnSuccess()
    {
        var health =
            new LocalVisualProviderHealthRegistry();

        health.RecordFailure(
            "windows-ocr",
            100,
            "acceptance failure");

        health.RecordSuccess(
            "windows-ocr",
            40,
            "acceptance success");

        var snapshot =
            health.Get(
                "windows-ocr");

        Require(
            snapshot.State ==
                LocalVisualProviderHealthState.Healthy &&
            snapshot.ConsecutiveFailures == 0 &&
            snapshot.LastLatencyMilliseconds == 40,
            "Local visual provider health chưa reset sau success.");
    }

    private static void CheckRuntimeRegressionCandidatePrivacy()
    {
        var store =
            new ComputerOperatorRegressionCandidateStore();

        var now = DateTimeOffset.UtcNow;
        var progress =
            new ComputerOperatorProgressSnapshot(
                Active: false,
                Paused: false,
                Status: "blocked",
                StartedAtUtc: now.AddSeconds(-5),
                UpdatedAtUtc: now,
                ObservationCount: 4,
                ActionCount: 1,
                CurrentStage: "blocked",
                Stale: false,
                StaleSeconds: 0,
                Entries:
                [
                    new ComputerOperatorProgressEntry(
                        now,
                        "verification-failed",
                        "Regression acceptance failure signal.")
                ]);

        const string rawGoal =
            "Mở ứng dụng thử nghiệm riêng tư";

        store.Capture(
            rawGoal,
            "blocked",
            "Không xác minh được expected effect.",
            progress,
            "Gemini",
            "acceptance-model");

        var snapshot = store.Get();
        var candidate = snapshot.Candidates.Single();

        Require(
            snapshot.Count == 1 &&
            candidate.GoalHash.Length == 64 &&
            candidate.GoalHash ==
                ComputerOperatorRegressionCandidateStore.HashGoalForAcceptance(
                    rawGoal) &&
            !candidate.Summary.Contains(
                rawGoal,
                StringComparison.OrdinalIgnoreCase) &&
            candidate.FailureSignals.Contains(
                "verification-failed",
                StringComparer.OrdinalIgnoreCase),
            "Runtime regression candidate chưa giữ đúng privacy fingerprint hoặc failure signal.");
    }

    private static void CheckRuntimeRegressionPromotionDraft()
    {
        var store =
            new ComputerOperatorRegressionCandidateStore();

        var now = DateTimeOffset.UtcNow;
        var progress =
            new ComputerOperatorProgressSnapshot(
                Active: false,
                Paused: false,
                Status: "blocked",
                StartedAtUtc: now.AddSeconds(-3),
                UpdatedAtUtc: now,
                ObservationCount: 3,
                ActionCount: 1,
                CurrentStage: "blocked",
                Stale: false,
                StaleSeconds: 0,
                Entries:
                [
                    new ComputerOperatorProgressEntry(
                        now,
                        "verification-failed",
                        "Acceptance signal.")
                ]);

        const string rawGoal =
            "Mở dữ liệu riêng tư cực kỳ nhạy cảm";

        store.Capture(
            rawGoal,
            "blocked",
            "Không xác minh được kết quả.",
            progress,
            "OpenAI",
            "acceptance-model");

        var candidate =
            store.Get().Candidates.Single();

        var draft =
            store.CreateDraft(candidate.Id);

        Require(
            draft is not null &&
            draft.RequiresReview &&
            draft.CandidateId == candidate.Id &&
            draft.Incident.RegressionCaseId == draft.Test.Id &&
            draft.Incident.Id.StartsWith(
                "incident-runtime-",
                StringComparison.Ordinal) &&
            !draft.Incident.Title.Contains(
                rawGoal,
                StringComparison.OrdinalIgnoreCase) &&
            !draft.Incident.ExpectedInvariant.Contains(
                rawGoal,
                StringComparison.OrdinalIgnoreCase) &&
            !draft.Test.ExpectedBehavior.Contains(
                rawGoal,
                StringComparison.OrdinalIgnoreCase),
            "Regression promotion draft chưa giữ review gate hoặc còn lộ raw goal.");
    }

    private static void CheckRuntimeRegressionReviewedPromotion()
    {
        var rejectedWithoutReview = false;

        try
        {
            ComputerOperatorRegressionPromotionService
                .EnsureConfirmedReviewForAcceptance(
                    confirmedReview: false);
        }
        catch (ComputerOperatorRegressionPromotionException)
        {
            rejectedWithoutReview = true;
        }

        var draft =
            new ComputerOperatorRegressionPromotionDraft(
                CandidateId: "runtime-acceptance-candidate",
                CreatedAtUtc: DateTimeOffset.UtcNow,
                RequiresReview: true,
                Incident:
                    new ComputerOperatorRegressionIncidentDraft(
                        Id: "incident-runtime-verification-failed-abcdef123456",
                        Title: "Runtime blocked: verification-failed",
                        Severity: "high",
                        Origin: "runtime Computer Operator regression candidate",
                        RegressionCaseId: "runtime-verification-failed-abcdef123456",
                        ExpectedInvariant: "Không tái diễn verification-failed."),
                Test:
                    new ComputerOperatorRegressionTestDraft(
                        Id: "runtime-verification-failed-abcdef123456",
                        Category: "computer-operator.runtime-regression",
                        Outcome: "blocked",
                        CurrentStage: "blocked",
                        FailureSignals: ["verification-failed"],
                        ExpectedBehavior: "Phải recovery an toàn thay vì lặp verification-failed."));

        var request =
            ComputerOperatorRegressionPromotionService
                .BuildCreateRequestForAcceptance(
                    draft);

        Require(
            rejectedWithoutReview &&
            request.Enabled &&
            request.Id == draft.Test.Id &&
            request.Category ==
                ComputerOperatorRegressionPromotionService.RuntimeRegressionCategory &&
            request.Metadata is not null &&
            request.Metadata.TryGetValue(
                "reviewed",
                out var reviewed) &&
            reviewed == "true" &&
            request.Metadata.TryGetValue(
                "incidentId",
                out var incidentId) &&
            incidentId == draft.Incident.Id,
            "Reviewed regression promotion chưa chặn thiếu xác nhận hoặc chưa đánh dấu metadata review đúng.");
    }

    private static void CheckRegressionReliabilityMetricsReadiness()
    {
        var now = DateTimeOffset.UtcNow;
        var lab =
            new PersonalAI.Web.Evaluation.Regression.ComputerOperatorRegressionLabReport(
                Version: "acceptance",
                StartedAtUtc: now.AddSeconds(-1),
                CompletedAtUtc: now,
                TotalCases: 6,
                PassedCases: 6,
                FailedCases: 0,
                PassRate: 1d,
                DurationMilliseconds: 10,
                Cases: Array.Empty<PersonalAI.Web.Evaluation.Regression.ComputerOperatorRegressionCaseResult>());

        var candidate =
            new ComputerOperatorRegressionCandidate(
                Id: "runtime-acceptance-abcdef123456",
                CapturedAtUtc: now,
                GoalHash: new string('a', 64),
                Outcome: "blocked",
                Summary: "Không xác minh được kết quả.",
                CurrentStage: "blocked",
                ObservationCount: 3,
                ActionCount: 1,
                Provider: "Gemini",
                Model: "acceptance-model",
                FailureSignals: ["verification-failed"]);

        var snapshot =
            new ComputerOperatorRegressionCandidateSnapshot(
                Count: 1,
                MaximumEntries: 50,
                Candidates: [candidate]);

        var withoutPromotion =
            ComputerOperatorRegressionMetricsService
                .CalculateForAcceptance(
                    lab,
                    snapshot,
                    new HashSet<string>(StringComparer.Ordinal));

        var promotedId =
            $"runtime-verification-failed-{candidate.GoalHash[..12]}";

        var withPromotion =
            ComputerOperatorRegressionMetricsService
                .CalculateForAcceptance(
                    lab,
                    snapshot,
                    new HashSet<string>(
                        [promotedId],
                        StringComparer.Ordinal));

        Require(
            withoutPromotion.BaselineCoverageHealthy &&
            !withoutPromotion.ReadyForExternalBenchmark &&
            withoutPromotion.RuntimeUnreviewedCandidates == 1 &&
            withoutPromotion.RuntimePromotionRate == 0d &&
            withPromotion.BaselineCoverageHealthy &&
            withPromotion.ReadyForExternalBenchmark &&
            withPromotion.RuntimeUnreviewedCandidates == 0 &&
            withPromotion.RuntimePromotionRate == 1d,
            "Regression reliability metrics chưa chặn/cho phép external benchmark đúng theo coverage và review state.");
    }

    private static void CheckRegressionReadinessGate()
    {
        var clean =
            new ComputerOperatorRegressionMetrics(
                Version: "acceptance",
                MeasuredAtUtc: DateTimeOffset.UtcNow,
                LabTotalCases: 6,
                LabPassedCases: 6,
                LabFailedCases: 0,
                LabPassRate: 1d,
                RuntimeCandidateCount: 1,
                RuntimePromotedCases: 1,
                RuntimeUnreviewedCandidates: 0,
                RuntimePromotionRate: 1d,
                BaselineCoverageHealthy: true,
                ReadyForExternalBenchmark: true,
                UnreviewedCandidateIds: Array.Empty<string>());

        var pass =
            ComputerOperatorRegressionReadinessGate
                .EvaluateForAcceptance(
                    clean);

        var dirty =
            clean with
            {
                RuntimeUnreviewedCandidates = 1,
                RuntimePromotionRate = 0d,
                ReadyForExternalBenchmark = false,
                UnreviewedCandidateIds = ["runtime-pending"]
            };

        var blocked =
            ComputerOperatorRegressionReadinessGate
                .EvaluateForAcceptance(
                    dirty);

        var partial =
            clean with
            {
                LabPassedCases = 5,
                LabFailedCases = 1,
                LabPassRate = 5d / 6d,
                BaselineCoverageHealthy = false,
                ReadyForExternalBenchmark = false
            };

        var partialBlocked =
            ComputerOperatorRegressionReadinessGate
                .EvaluateForAcceptance(
                    partial);

        Require(
            pass.Passed &&
            pass.Status == "pass" &&
            pass.Reasons.Count == 0 &&
            !blocked.Passed &&
            blocked.Status == "blocked" &&
            blocked.Reasons.Count >= 2 &&
            !partialBlocked.Passed &&
            partialBlocked.Reasons.Any(reason =>
                reason.Contains(
                    "100%",
                    StringComparison.OrdinalIgnoreCase)),
            "Regression readiness gate chưa phân biệt PASS/BLOCKED đúng theo ngưỡng reliability.");
    }

    private static void CheckExternalBenchmarkPreparationContract()
    {
        var metrics =
            new ComputerOperatorRegressionMetrics(
                Version: "acceptance",
                MeasuredAtUtc: DateTimeOffset.UtcNow,
                LabTotalCases: 6,
                LabPassedCases: 6,
                LabFailedCases: 0,
                LabPassRate: 1d,
                RuntimeCandidateCount: 0,
                RuntimePromotedCases: 0,
                RuntimeUnreviewedCandidates: 0,
                RuntimePromotionRate: 1d,
                BaselineCoverageHealthy: true,
                ReadyForExternalBenchmark: true,
                UnreviewedCandidateIds: Array.Empty<string>());

        var passingGate =
            new ComputerOperatorRegressionReadinessGateResult(
                Version: "acceptance",
                EvaluatedAtUtc: DateTimeOffset.UtcNow,
                Passed: true,
                Status: "pass",
                Reasons: Array.Empty<string>(),
                Metrics: metrics);

        ComputerOperatorExternalBenchmarkService
            .EnsureReadinessForAcceptance(
                passingGate);

        var blockedRejected = false;
        try
        {
            ComputerOperatorExternalBenchmarkService
                .EnsureReadinessForAcceptance(
                    passingGate with
                    {
                        Passed = false,
                        Status = "blocked",
                        Reasons = ["Regression chưa sạch."]
                    });
        }
        catch (PersonalAI.Web.Evaluation.Benchmarks.ComputerOperatorExternalBenchmarkValidationException)
        {
            blockedRejected = true;
        }

        var input =
            JsonSerializer.SerializeToElement(
                new
                {
                    goal = "Mở Notepad và gõ một câu ngắn.",
                    expectedEffect = "Notepad hiển thị đúng nội dung đã gõ.",
                    maximumSteps = 12,
                    requiresInteractiveDesktop = true
                });

        var expected =
            JsonSerializer.SerializeToElement(
                new
                {
                    completed = true
                });

        var item =
            new PersonalAI.Web.Evaluation.Regression.RegressionDatasetItem(
                Id: "external-notepad-basic",
                Title: "Notepad basic interaction",
                Category: ComputerOperatorExternalBenchmarkService.BenchmarkCategory,
                Input: input,
                Expected: expected,
                Metadata: new Dictionary<string, string>(),
                Enabled: true,
                WorkspaceId: "personal",
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow);

        var plan =
            ComputerOperatorExternalBenchmarkService
                .ParseCaseForAcceptance(
                    item);

        Require(
            blockedRejected &&
            plan.CaseId == item.Id &&
            plan.Goal.Contains(
                "Notepad",
                StringComparison.OrdinalIgnoreCase) &&
            plan.ExpectedEffect.Length > 0 &&
            plan.MaximumSteps == 12 &&
            plan.RequiresInteractiveDesktop,
            "External benchmark preparation contract chưa chặn readiness lỗi hoặc parse case chưa đúng.");
    }

    private static void CheckExternalBenchmarkScenarioPackContract()
    {
        var pack =
            PersonalAI.Web.Evaluation.Benchmarks
                .ComputerOperatorBenchmarkScenarioCatalog
                .GetPack();

        var cases =
            PersonalAI.Web.Evaluation.Benchmarks
                .ComputerOperatorBenchmarkScenarioCatalog
                .BuildRegressionCases();

        var rejectedWithoutConfirmation = false;
        try
        {
            ComputerOperatorBenchmarkScenarioPackService
                .EnsureConfirmedForAcceptance(
                    confirmed: false);
        }
        catch (ComputerOperatorBenchmarkScenarioPackException)
        {
            rejectedWithoutConfirmation = true;
        }

        var ids =
            cases.Select(item => item.Id).ToArray();

        Require(
            rejectedWithoutConfirmation &&
            pack.Version ==
                PersonalAI.Web.Evaluation.Benchmarks
                    .ComputerOperatorBenchmarkScenarioCatalog
                    .PackVersion &&
            pack.Platform == "windows" &&
            pack.Scenarios.Count ==
                PersonalAI.Web.Evaluation.Benchmarks
                    .ComputerOperatorBenchmarkScenarioCatalog
                    .ScenarioCount &&
            cases.Count ==
                PersonalAI.Web.Evaluation.Benchmarks
                    .ComputerOperatorBenchmarkScenarioCatalog
                    .ScenarioCount &&
            ids.Distinct(StringComparer.Ordinal).Count() == ids.Length &&
            cases.All(item =>
                item.Enabled &&
                item.Category ==
                    ComputerOperatorExternalBenchmarkService
                        .BenchmarkCategory &&
                item.Metadata is not null &&
                item.Metadata.TryGetValue(
                    "risk",
                    out var risk) &&
                risk == "low") &&
            pack.Scenarios.All(scenario =>
                scenario.MaximumSteps is >= 1 and <=
                    ComputerOperatorExternalBenchmarkService
                        .HardMaximumSteps &&
                scenario.RequiresInteractiveDesktop &&
                scenario.Goal.Length >= 2 &&
                scenario.ExpectedEffect.Length >= 2),
            "External benchmark Scenario Pack chưa đảm bảo đủ 6 case an toàn, duy nhất hoặc confirmation gate.");
    }

    private static void CheckExternalBenchmarkRunnerContract()
    {
        var rejectedWithoutConfirmation = false;
        try
        {
            ComputerOperatorExternalBenchmarkRunner
                .EnsureConfirmedForAcceptance(
                    confirmed: false);
        }
        catch (PersonalAI.Web.Evaluation.Benchmarks.ComputerOperatorExternalBenchmarkValidationException)
        {
            rejectedWithoutConfirmation = true;
        }

        var rejectedWithoutInteractiveDesktop = false;
        try
        {
            ComputerOperatorExternalBenchmarkRunner
                .EnsureInteractiveDesktopForAcceptance(
                    supported: true,
                    interactiveSession: false);
        }
        catch (PersonalAI.Web.Evaluation.Benchmarks.ComputerOperatorExternalBenchmarkValidationException)
        {
            rejectedWithoutInteractiveDesktop = true;
        }

        ComputerOperatorExternalBenchmarkRunner
            .EnsureInteractiveDesktopForAcceptance(
                supported: true,
                interactiveSession: true);

        var ready =
            ComputerOperatorExternalBenchmarkRunner
                .IsReadyForIndependentEvaluationForAcceptance(
                    taskCompleted: true,
                    recordedSteps: 10,
                    maximumSteps: 24);

        var notReadyWhenIncomplete =
            !ComputerOperatorExternalBenchmarkRunner
                .IsReadyForIndependentEvaluationForAcceptance(
                    taskCompleted: false,
                    recordedSteps: 10,
                    maximumSteps: 24);

        var notReadyWhenOverBudget =
            !ComputerOperatorExternalBenchmarkRunner
                .IsReadyForIndependentEvaluationForAcceptance(
                    taskCompleted: true,
                    recordedSteps: 25,
                    maximumSteps: 24);

        var notReadyWithoutVerifiedAction =
            !ComputerOperatorExternalBenchmarkRunner
                .IsReadyForIndependentEvaluationForAcceptance(
                    taskCompleted: true,
                    recordedSteps: 10,
                    maximumSteps: 24,
                    verifiedActionCount: 0,
                    verificationFailureCount: 0,
                    recoveryCount: 0);

        var notReadyWhenFailureHasNoRecovery =
            !ComputerOperatorExternalBenchmarkRunner
                .IsReadyForIndependentEvaluationForAcceptance(
                    taskCompleted: true,
                    recordedSteps: 10,
                    maximumSteps: 24,
                    verifiedActionCount: 6,
                    verificationFailureCount: 2,
                    recoveryCount: 1);

        var readyWhenFailuresRecovered =
            ComputerOperatorExternalBenchmarkRunner
                .IsReadyForIndependentEvaluationForAcceptance(
                    taskCompleted: true,
                    recordedSteps: 10,
                    maximumSteps: 24,
                    verifiedActionCount: 6,
                    verificationFailureCount: 2,
                    recoveryCount: 2);

        Require(
            rejectedWithoutConfirmation &&
            rejectedWithoutInteractiveDesktop &&
            ready &&
            notReadyWhenIncomplete &&
            notReadyWhenOverBudget &&
            notReadyWithoutVerifiedAction &&
            notReadyWhenFailureHasNoRecovery &&
            readyWhenFailuresRecovered &&
            ComputerOperatorExternalBenchmarkRunner.HardTimeoutSeconds >= 120,
            "External benchmark runner chưa khóa confirmation/interactive desktop hoặc VERIFY/RECOVERY readiness semantics chưa đúng.");
    }

    private static void CheckExternalBenchmarkIndependentEvaluationContract()
    {
        var onlyTaskCompleted =
            ComputerOperatorExternalBenchmarkEvaluationService
                .DeterminePassedForAcceptance(
                    taskCompleted: true,
                    withinStepBudget: true,
                    expectedEffectObserved: false);

        var observedButExecutionInvalid =
            ComputerOperatorExternalBenchmarkEvaluationService
                .DeterminePassedForAcceptance(
                    taskCompleted: false,
                    withinStepBudget: true,
                    expectedEffectObserved: true);

        var passed =
            ComputerOperatorExternalBenchmarkEvaluationService
                .DeterminePassedForAcceptance(
                    taskCompleted: true,
                    withinStepBudget: true,
                    expectedEffectObserved: true);

        var rejectedWithoutVerifiedAction =
            !ComputerOperatorExternalBenchmarkEvaluationService
                .DeterminePassedForAcceptance(
                    taskCompleted: true,
                    withinStepBudget: true,
                    expectedEffectObserved: true,
                    verifiedActionCount: 0,
                    verificationFailureCount: 0,
                    recoveryCount: 0);

        var rejectedWhenFailureNotRecovered =
            !ComputerOperatorExternalBenchmarkEvaluationService
                .DeterminePassedForAcceptance(
                    taskCompleted: true,
                    withinStepBudget: true,
                    expectedEffectObserved: true,
                    verifiedActionCount: 5,
                    verificationFailureCount: 2,
                    recoveryCount: 1);

        var passedWhenFailureRecovered =
            ComputerOperatorExternalBenchmarkEvaluationService
                .DeterminePassedForAcceptance(
                    taskCompleted: true,
                    withinStepBudget: true,
                    expectedEffectObserved: true,
                    verifiedActionCount: 5,
                    verificationFailureCount: 2,
                    recoveryCount: 2);

        Require(
            !onlyTaskCompleted &&
            !observedButExecutionInvalid &&
            passed &&
            rejectedWithoutVerifiedAction &&
            rejectedWhenFailureNotRecovered &&
            passedWhenFailureRecovered,
            "Independent benchmark evaluation phải cần execution hợp lệ, external evidence và VERIFY/RECOVERY metrics nhất quán.");
    }

    private static void CheckRuntimeStateIntelligenceContract()
    {
        var intelligence =
            new ComputerOperatorRuntimeStateIntelligence();

        var progressing =
            intelligence.Classify(
                new(
                    ExpectedEffectObserved: false,
                    ExpectedEffectConfidence: 0,
                    ExplicitFailureObserved: false,
                    FailureConfidence: 0,
                    BlockingConditionObserved: false,
                    BlockingConfidence: 0,
                    MeaningfulProgressObserved: true,
                    ProgressConfidence: 0.92,
                    ExternalWaitObserved: false,
                    ExternalWaitConfidence: 0,
                    ProcessAlive: true,
                    ProcessResponding: true,
                    ResourceActivityScore: 0.45,
                    Elapsed: TimeSpan.FromSeconds(45),
                    TimeSinceMeaningfulProgress: TimeSpan.FromSeconds(1)));

        var slowButAlive =
            intelligence.Classify(
                new(
                    ExpectedEffectObserved: false,
                    ExpectedEffectConfidence: 0,
                    ExplicitFailureObserved: false,
                    FailureConfidence: 0,
                    BlockingConditionObserved: false,
                    BlockingConfidence: 0,
                    MeaningfulProgressObserved: false,
                    ProgressConfidence: 0,
                    ExternalWaitObserved: false,
                    ExternalWaitConfidence: 0,
                    ProcessAlive: true,
                    ProcessResponding: true,
                    ResourceActivityScore: 0.35,
                    Elapsed: TimeSpan.FromSeconds(70),
                    TimeSinceMeaningfulProgress: TimeSpan.FromSeconds(20)));

        var hung =
            intelligence.Classify(
                new(
                    ExpectedEffectObserved: false,
                    ExpectedEffectConfidence: 0,
                    ExplicitFailureObserved: false,
                    FailureConfidence: 0,
                    BlockingConditionObserved: false,
                    BlockingConfidence: 0,
                    MeaningfulProgressObserved: false,
                    ProgressConfidence: 0,
                    ExternalWaitObserved: false,
                    ExternalWaitConfidence: 0,
                    ProcessAlive: true,
                    ProcessResponding: false,
                    ResourceActivityScore: 0.01,
                    Elapsed: TimeSpan.FromSeconds(90),
                    TimeSinceMeaningfulProgress: TimeSpan.FromSeconds(45)));

        var failed =
            intelligence.Classify(
                new(
                    ExpectedEffectObserved: false,
                    ExpectedEffectConfidence: 0,
                    ExplicitFailureObserved: true,
                    FailureConfidence: 0.95,
                    BlockingConditionObserved: false,
                    BlockingConfidence: 0,
                    MeaningfulProgressObserved: false,
                    ProgressConfidence: 0,
                    ExternalWaitObserved: false,
                    ExternalWaitConfidence: 0,
                    ProcessAlive: false,
                    ProcessResponding: null,
                    ResourceActivityScore: 0,
                    Elapsed: TimeSpan.FromSeconds(5),
                    TimeSinceMeaningfulProgress: TimeSpan.FromSeconds(5)));

        Require(
            progressing.State ==
                ComputerOperatorRuntimeStates.Progressing &&
            progressing.RecommendedDecision ==
                ComputerOperatorRuntimeDecisions.Wait &&
            slowButAlive.State ==
                ComputerOperatorRuntimeStates.WaitingExpected &&
            hung.State ==
                ComputerOperatorRuntimeStates.Hung &&
            hung.RecommendedDecision ==
                ComputerOperatorRuntimeDecisions.Recover &&
            failed.State ==
                ComputerOperatorRuntimeStates.Failed,
            "Runtime State Intelligence phải ưu tiên evidence: tiến triển thì chờ, app chậm còn hoạt động thì không fail, treo thật mới recover.");
    }

    private static void CheckProgressIntelligenceContract()
    {
        var runtime =
            new ComputerOperatorRuntimeStateIntelligence();

        var intelligence =
            new ComputerOperatorProgressIntelligence(runtime);

        var corroborated =
            intelligence.Assess(
                new(
                    ExpectedEffectObserved: false,
                    ExpectedEffectConfidence: 0,
                    ExplicitFailureObserved: false,
                    FailureConfidence: 0,
                    BlockingConditionObserved: false,
                    BlockingConfidence: 0,
                    ExternalWaitObserved: false,
                    ExternalWaitConfidence: 0,
                    ProcessAlive: true,
                    ProcessResponding: true,
                    CpuActivityScore: 0.15,
                    DiskActivityScore: 0.10,
                    NetworkActivityScore: 0.05,
                    WindowTransitionScore: 0.62,
                    StructuredUiChangeScore: 0.58,
                    VisualChangeScore: 0.20,
                    RelevantSystemEventScore: 0.50,
                    Elapsed: TimeSpan.FromSeconds(12),
                    TimeSinceMeaningfulProgress: TimeSpan.FromSeconds(1)));

        var resourceOnly =
            intelligence.Assess(
                new(
                    ExpectedEffectObserved: false,
                    ExpectedEffectConfidence: 0,
                    ExplicitFailureObserved: false,
                    FailureConfidence: 0,
                    BlockingConditionObserved: false,
                    BlockingConfidence: 0,
                    ExternalWaitObserved: false,
                    ExternalWaitConfidence: 0,
                    ProcessAlive: true,
                    ProcessResponding: true,
                    CpuActivityScore: 0.80,
                    DiskActivityScore: 0.55,
                    NetworkActivityScore: 0.10,
                    WindowTransitionScore: 0.05,
                    StructuredUiChangeScore: 0.05,
                    VisualChangeScore: 0.05,
                    RelevantSystemEventScore: 0.05,
                    Elapsed: TimeSpan.FromSeconds(40),
                    TimeSinceMeaningfulProgress: TimeSpan.FromSeconds(20)));

        var hung =
            intelligence.Assess(
                new(
                    ExpectedEffectObserved: false,
                    ExpectedEffectConfidence: 0,
                    ExplicitFailureObserved: false,
                    FailureConfidence: 0,
                    BlockingConditionObserved: false,
                    BlockingConfidence: 0,
                    ExternalWaitObserved: false,
                    ExternalWaitConfidence: 0,
                    ProcessAlive: true,
                    ProcessResponding: false,
                    CpuActivityScore: 0.01,
                    DiskActivityScore: 0.01,
                    NetworkActivityScore: 0.01,
                    WindowTransitionScore: 0.01,
                    StructuredUiChangeScore: 0.01,
                    VisualChangeScore: 0.01,
                    RelevantSystemEventScore: 0.01,
                    Elapsed: TimeSpan.FromSeconds(90),
                    TimeSinceMeaningfulProgress: TimeSpan.FromSeconds(45)));

        Require(
            corroborated.MeaningfulProgress &&
            corroborated.RuntimeState.State ==
                ComputerOperatorRuntimeStates.Progressing &&
            corroborated.AdaptiveSample.Status ==
                AdaptiveWaitStatuses.Progressing &&
            !resourceOnly.MeaningfulProgress &&
            resourceOnly.RuntimeState.State ==
                ComputerOperatorRuntimeStates.WaitingExpected &&
            resourceOnly.AdaptiveSample.Status ==
                AdaptiveWaitStatuses.Pending &&
            hung.RuntimeState.State ==
                ComputerOperatorRuntimeStates.Hung &&
            hung.AdaptiveSample.Status ==
                AdaptiveWaitStatuses.Stalled,
            "Progress Intelligence phải dùng corroboration cho progress, không dùng CPU/Disk làm PASS, và treo thật phải chuyển sang stalled.");
    }

    private static void CheckLearnedTimingContract()
    {
        var sorted =
            new double[]
            {
                1000,
                1200,
                1500,
                2000,
                4000
            };

        var median =
            ComputerOperatorLearnedTimingService
                .Percentile(
                    sorted,
                    0.50);

        var p95 =
            ComputerOperatorLearnedTimingService
                .Percentile(
                    sorted,
                    0.95);

        var fallback =
            ComputerOperatorAdaptiveWaitPolicy
                .ForAction("click-left");

        var resolverNotReady =
            new ComputerOperatorAdaptiveWaitPolicyResolver(
                new AcceptanceLearnedTimingService(
                    new(
                        ComputerOperatorTelemetryStages.AdaptiveWait,
                        "click-left",
                        4,
                        45000,
                        60000,
                        70000,
                        70000,
                        0.20,
                        Ready: false)));

        var notReadyPolicy =
            resolverNotReady.Resolve(
                "click-left");

        var resolverReady =
            new ComputerOperatorAdaptiveWaitPolicyResolver(
                new AcceptanceLearnedTimingService(
                    new(
                        ComputerOperatorTelemetryStages.AdaptiveWait,
                        "click-left",
                        20,
                        90000,
                        120000,
                        140000,
                        150000,
                        1.0,
                        Ready: true)));

        var readyPolicy =
            resolverReady.Resolve(
                "click-left");

        Require(
            Math.Abs(median - 1500) < 0.01 &&
            p95 > median &&
            notReadyPolicy.AbsoluteTimeout ==
                fallback.AbsoluteTimeout &&
            readyPolicy.AbsoluteTimeout >
                fallback.AbsoluteTimeout &&
            readyPolicy.AbsoluteTimeout <=
                TimeSpan.FromMinutes(5) &&
            readyPolicy.StallTimeout <
                readyPolicy.AbsoluteTimeout,
            "Learned Timing phải fallback khi chưa đủ mẫu, ưu tiên P95 khi đủ mẫu và luôn giữ timeout hữu hạn.");
    }

    private static void CheckExperienceDatabaseFoundation()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "personalai-experience-acceptance",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var workspace =
                new AcceptanceWorkspaceContextAccessor(
                    PersonalWorkspaceIds.PersonalAi);

            var repository =
                new SqliteComputerOperatorExperienceRepository(
                    configuration,
                    workspace);

            var fingerprint =
                repository.FingerprintContext(
                    "native-app-startup|waiting|process-alive");

            var failure =
                repository.Append(
                    ComputerOperatorExperienceKinds.Failure,
                    fingerprint,
                    failureCode: "no-progress",
                    strategyKey: "observe-first",
                    outcome: "recoverable",
                    durationMilliseconds: 42000,
                    confidence: 0.82,
                    verified: false);

            var recovery =
                repository.Append(
                    ComputerOperatorExperienceKinds.Recovery,
                    fingerprint,
                    failureCode: "no-progress",
                    strategyKey: "wait-with-runtime-evidence",
                    outcome: "verified-pass",
                    durationMilliseconds: 51000,
                    confidence: 0.93,
                    verified: true);

            var recent =
                repository.GetRecent(10);

            var diagnostics =
                repository.GetDiagnostics();

            Require(
                fingerprint.Length == 64 &&
                failure.WorkspaceId ==
                    PersonalWorkspaceIds.PersonalAi &&
                recovery.Verified &&
                recent.Count == 2 &&
                diagnostics.Provider == "sqlite" &&
                diagnostics.SchemaVersion ==
                    SqliteComputerOperatorExperienceRepository.SchemaVersion &&
                diagnostics.UsesWal &&
                diagnostics.EventCount == 2 &&
                diagnostics.VerifiedEventCount == 1 &&
                diagnostics.FailureCount == 1 &&
                diagnostics.RecoveryCount == 1,
                "Experience DB phải ghi/đọc được bằng SQLite WAL, cô lập theo workspace và chỉ dùng context fingerprint.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // File lock cleanup không được làm acceptance fail.
            }
        }
    }

    private static void CheckVerifiedRecoveryLearningCandidate()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "personalai-learning-acceptance",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var workspace =
                new AcceptanceWorkspaceContextAccessor(
                    PersonalWorkspaceIds.PersonalAi);

            var repository =
                new SqliteComputerOperatorExperienceRepository(
                    configuration,
                    workspace);

            var learning =
                new ComputerOperatorLearningSession(
                    repository);

            var noFailure =
                learning.TryRecordVerifiedRecovery(
                    "strategy-before-failure",
                    0.99);

            learning.RecordFailure(
                "scene=abc|state=no-effect",
                "action-no-effect",
                "strategy-a",
                0.84);

            var candidate =
                learning.TryRecordVerifiedRecovery(
                    "strategy-b",
                    0.96);

            var secondPass =
                learning.TryRecordVerifiedRecovery(
                    "strategy-c",
                    0.99);

            var recent =
                repository.GetRecent(10);

            var diagnostics =
                repository.GetDiagnostics();

            var failure =
                recent.Single(item =>
                    item.Kind ==
                    ComputerOperatorExperienceKinds.Failure);

            var recovery =
                recent.Single(item =>
                    item.Kind ==
                    ComputerOperatorExperienceKinds.Recovery);

            var pendingCandidate =
                recent.Single(item =>
                    item.Kind ==
                    ComputerOperatorExperienceKinds.Candidate);

            Require(
                !noFailure.Created &&
                candidate.Created &&
                !secondPass.Created &&
                recent.Count == 3 &&
                failure.ContextFingerprint ==
                    recovery.ContextFingerprint &&
                recovery.ContextFingerprint ==
                    pendingCandidate.ContextFingerprint &&
                recovery.Verified &&
                !pendingCandidate.Verified &&
                pendingCandidate.Confidence <= 0.60 &&
                diagnostics.FailureCount == 1 &&
                diagnostics.RecoveryCount == 1 &&
                diagnostics.CandidateCount == 1,
                "Learning chỉ được tạo một candidate chưa trusted sau failure thật và recovery đã VERIFY PASS.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Cleanup thư mục tạm không được làm acceptance fail.
            }
        }
    }

    private static void CheckExperienceValidatorPromotion()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "personalai-validator-acceptance",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var workspace =
                new AcceptanceWorkspaceContextAccessor(
                    PersonalWorkspaceIds.PersonalAi);

            var repository =
                new SqliteComputerOperatorExperienceRepository(
                    configuration,
                    workspace);

            var validator =
                new ComputerOperatorExperienceValidator(
                    repository);

            var firstLearning =
                new ComputerOperatorLearningSession(
                    repository);

            firstLearning.RecordFailure(
                "scene=stable-context|state=no-effect",
                "action-no-effect",
                "strategy-a",
                0.86);

            var firstCandidate =
                firstLearning.TryRecordVerifiedRecovery(
                    "strategy-b",
                    0.94);

            Require(
                firstCandidate.CandidateId is Guid,
                "Lần recovery đầu phải tạo candidate để validator kiểm tra.");

            var firstValidation =
                validator.Validate(
                    firstCandidate.CandidateId!.Value);

            var secondLearning =
                new ComputerOperatorLearningSession(
                    repository);

            secondLearning.RecordFailure(
                "scene=stable-context|state=no-effect",
                "action-no-effect",
                "strategy-a",
                0.88);

            var secondCandidate =
                secondLearning.TryRecordVerifiedRecovery(
                    "strategy-b",
                    0.96);

            Require(
                secondCandidate.CandidateId is Guid,
                "Lần recovery thứ hai phải tạo candidate.");

            var secondValidation =
                validator.Validate(
                    secondCandidate.CandidateId!.Value);

            var diagnostics =
                repository.GetDiagnostics();

            Require(
                firstValidation.Status ==
                    ComputerOperatorExperienceValidationStatuses.Pending &&
                !firstValidation.Promoted &&
                firstValidation.VerifiedRecoveryCount == 1 &&
                secondValidation.Status ==
                    ComputerOperatorExperienceValidationStatuses.Promoted &&
                secondValidation.Promoted &&
                secondValidation.VerifiedRecoveryCount >=
                    ComputerOperatorExperienceValidator.MinimumVerifiedRecoveries &&
                secondValidation.Confidence >=
                    ComputerOperatorExperienceValidator.MinimumAverageRecoveryConfidence &&
                diagnostics.ExperienceCount == 1,
                "Validator phải giữ candidate đầu ở pending và chỉ promote trusted experience sau recovery lặp lại đủ confidence.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Cleanup thư mục tạm không được làm acceptance fail.
            }
        }
    }

    private static void CheckExperienceConsolidationAndPersistentTiming()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "personalai-consolidation-acceptance",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var workspace =
                new AcceptanceWorkspaceContextAccessor(
                    PersonalWorkspaceIds.PersonalAi);

            var repository =
                new SqliteComputerOperatorExperienceRepository(
                    configuration,
                    workspace);

            var aggregates =
                new SqliteComputerOperatorExperienceAggregateStore(
                    configuration,
                    workspace);

            var fingerprint =
                repository.FingerprintContext(
                    "scene=stable|failure=no-effect");

            _ =
                repository.Append(
                    ComputerOperatorExperienceKinds.Experience,
                    fingerprint,
                    failureCode: "action-no-effect",
                    strategyKey: "wait-and-reobserve",
                    outcome: "trusted-verified",
                    durationMilliseconds: 40000,
                    confidence: 0.90,
                    verified: true);

            _ =
                repository.Append(
                    ComputerOperatorExperienceKinds.Experience,
                    fingerprint,
                    failureCode: "action-no-effect",
                    strategyKey: "wait-and-reobserve",
                    outcome: "trusted-verified",
                    durationMilliseconds: 50000,
                    confidence: 0.94,
                    verified: true);

            var consolidator =
                new ComputerOperatorExperienceConsolidator(
                    repository,
                    aggregates);

            var consolidation =
                consolidator.Consolidate();

            var patterns =
                aggregates.GetPatterns();

            aggregates.UpsertTimingProfile(
                new(
                    ComputerOperatorTelemetryStages.AdaptiveWait,
                    "click-left",
                    12,
                    42000,
                    61000,
                    68000,
                    72000,
                    0.90,
                    DateTimeOffset.UtcNow));

            var afterRestart =
                new ComputerOperatorLearnedTimingService(
                    new ComputerOperatorTelemetry(),
                    new SqliteComputerOperatorExperienceAggregateStore(
                        configuration,
                        workspace));

            var persistent =
                afterRestart.GetProfile(
                    ComputerOperatorTelemetryStages.AdaptiveWait,
                    "click-left");

            Require(
                consolidation.TrustedExperiencesScanned == 2 &&
                consolidation.PatternsWritten == 1 &&
                patterns.Count == 1 &&
                patterns[0].SampleCount == 2 &&
                Math.Abs(
                    patterns[0].AverageConfidence - 0.92) < 0.001 &&
                patterns[0].AverageDurationMilliseconds == 45000 &&
                persistent.SampleCount == 12 &&
                persistent.Ready &&
                Math.Abs(
                    persistent.P95Milliseconds - 68000) < 0.001,
                "Consolidation phải gom experience cùng context/failure/strategy thành một pattern và timing aggregate phải đọc lại được khi telemetry memory trống.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Cleanup thư mục tạm không được làm acceptance fail.
            }
        }
    }

    private static void CheckExperienceRetentionArchiveCleanup()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "personalai-lifecycle-acceptance",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var workspace =
                new AcceptanceWorkspaceContextAccessor(
                    PersonalWorkspaceIds.PersonalAi);

            var repository =
                new SqliteComputerOperatorExperienceRepository(
                    configuration,
                    workspace);

            var aggregates =
                new SqliteComputerOperatorExperienceAggregateStore(
                    configuration,
                    workspace);

            var fingerprint =
                repository.FingerprintContext(
                    "scene=lifecycle|failure=no-effect");

            var candidate =
                repository.Append(
                    ComputerOperatorExperienceKinds.Candidate,
                    fingerprint,
                    failureCode: "action-no-effect",
                    strategyKey: "strategy-b",
                    outcome: "awaiting-validation",
                    confidence: 0.50,
                    verified: false);

            var failure =
                repository.Append(
                    ComputerOperatorExperienceKinds.Failure,
                    fingerprint,
                    failureCode: "action-no-effect",
                    strategyKey: "strategy-a",
                    outcome: "observed-failure",
                    confidence: 0.80,
                    verified: false);

            var recovery =
                repository.Append(
                    ComputerOperatorExperienceKinds.Recovery,
                    fingerprint,
                    failureCode: "action-no-effect",
                    strategyKey: "strategy-b",
                    outcome: "verified-pass",
                    confidence: 0.94,
                    verified: true);

            var experience =
                repository.Append(
                    ComputerOperatorExperienceKinds.Experience,
                    fingerprint,
                    failureCode: "action-no-effect",
                    strategyKey: "strategy-b",
                    outcome: "trusted-verified",
                    confidence: 0.93,
                    verified: true);

            var strategy =
                repository.Append(
                    ComputerOperatorExperienceKinds.Strategy,
                    fingerprint,
                    failureCode: "action-no-effect",
                    strategyKey: "strategy-b",
                    outcome: "historical-strategy",
                    confidence: 0.90,
                    verified: true);

            aggregates.UpsertPattern(
                new(
                    fingerprint,
                    "action-no-effect",
                    "strategy-b",
                    2,
                    0.93,
                    45000,
                    DateTimeOffset.UtcNow));

            var hotPath =
                Path.Combine(
                    root,
                    "computer-operator-experience.db");

            using (var connection =
                new Microsoft.Data.Sqlite.SqliteConnection(
                    $"Data Source={hotPath};Mode=ReadWriteCreate;Cache=Shared"))
            {
                connection.Open();

                static void Age(
                    Microsoft.Data.Sqlite.SqliteConnection connection,
                    Guid id,
                    DateTimeOffset createdAt)
                {
                    using var command =
                        connection.CreateCommand();

                    command.CommandText =
                        """
                        UPDATE computer_operator_experience_events
                        SET created_at = $createdAt
                        WHERE event_id = $id;
                        """;

                    command.Parameters.AddWithValue(
                        "$createdAt",
                        createdAt.ToString("O"));
                    command.Parameters.AddWithValue(
                        "$id",
                        id.ToString("D"));
                    command.ExecuteNonQuery();
                }

                var now =
                    DateTimeOffset.UtcNow;

                Age(connection, candidate.Id, now.AddDays(-15));
                Age(connection, failure.Id, now.AddDays(-31));
                Age(connection, recovery.Id, now.AddDays(-91));
                Age(connection, experience.Id, now.AddDays(-181));
                Age(connection, strategy.Id, now.AddDays(-181));
            }

            var lifecycle =
                new ComputerOperatorExperienceLifecycleService(
                    configuration,
                    workspace);

            var result =
                lifecycle.RunMaintenance(
                    DateTimeOffset.UtcNow);

            var diagnostics =
                repository.GetDiagnostics();

            var archivePath =
                Path.Combine(
                    root,
                    "computer-operator-experience-archive.db");

            var archivedCount = 0;
            var archivedCandidates = 0;

            using (var archive =
                new Microsoft.Data.Sqlite.SqliteConnection(
                    $"Data Source={archivePath};Mode=ReadOnly;Cache=Shared"))
            {
                archive.Open();

                using var command =
                    archive.CreateCommand();

                command.CommandText =
                    """
                    SELECT
                        COUNT(*),
                        SUM(CASE WHEN kind = 'candidate' THEN 1 ELSE 0 END)
                    FROM computer_operator_experience_archive
                    WHERE workspace_id = $workspaceId;
                    """;

                command.Parameters.AddWithValue(
                    "$workspaceId",
                    PersonalWorkspaceIds.PersonalAi);

                using var reader =
                    command.ExecuteReader();

                if (reader.Read())
                {
                    archivedCount =
                        reader.IsDBNull(0)
                            ? 0
                            : reader.GetInt32(0);

                    archivedCandidates =
                        reader.IsDBNull(1)
                            ? 0
                            : reader.GetInt32(1);
                }
            }

            Require(
                result.DeletedCandidates == 1 &&
                result.ArchivedFailures == 1 &&
                result.ArchivedRecoveries == 1 &&
                result.ArchivedExperiences == 1 &&
                result.ArchivedStrategies == 1 &&
                result.RemainingHotEvents == 0 &&
                diagnostics.EventCount == 0 &&
                archivedCount == 4 &&
                archivedCandidates == 0,
                "Lifecycle phải xóa candidate chưa trusted, archive raw history cũ, giữ candidate ngoài archive và chỉ archive trusted experience khi pattern đã tồn tại.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Cleanup thư mục tạm không được làm acceptance fail.
            }
        }
    }

    private static void CheckVerifiedTransitionMemoryKeepsPartialSuccess()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "personalai-transition-memory-acceptance",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var workspace =
                new AcceptanceWorkspaceContextAccessor(
                    PersonalWorkspaceIds.PersonalAi);

            var repository =
                new SqliteComputerOperatorExperienceRepository(
                    configuration,
                    workspace);

            var transitions =
                new SqliteComputerOperatorVerifiedTransitionStore(
                    configuration,
                    workspace);

            var stateA =
                repository.FingerprintContext(
                    "state=a");

            var stateB =
                repository.FingerprintContext(
                    "state=b");

            var stepA =
                transitions.RecordVerified(
                    stateA,
                    "strategy-a-to-b",
                    "state b reached",
                    0.96,
                    120);

            var stepB =
                transitions.RecordVerified(
                    stateB,
                    "strategy-b-to-c",
                    "state c reached",
                    0.94,
                    180);

            _ =
                repository.Append(
                    ComputerOperatorExperienceKinds.Failure,
                    repository.FingerprintContext(
                        "state=c"),
                    failureCode: "verification-failed",
                    strategyKey: "strategy-c-to-d",
                    outcome: "observed-failure",
                    confidence: 0.77,
                    verified: false);

            var stateATransitions =
                transitions.FindExact(
                    stateA);

            var stateBTransitions =
                transitions.FindExact(
                    stateB);

            var repeatedA =
                transitions.RecordVerified(
                    stateA,
                    "strategy-a-to-b",
                    "state b reached",
                    0.98,
                    100);

            var all =
                transitions.GetRecent();

            Require(
                stepA.SuccessCount == 1 &&
                stepB.SuccessCount == 1 &&
                stateATransitions.Count == 1 &&
                stateBTransitions.Count == 1 &&
                repeatedA.SuccessCount == 2 &&
                repeatedA.AverageConfidence > 0.96 &&
                all.Count == 2 &&
                all.All(item =>
                    item.FromStateFingerprint.Length == 64 &&
                    item.ExpectedEffectFingerprint.Length == 64),
                "Failure ở bước C không được xóa transition A-B/B-C đã VERIFY PASS; transition lặp lại phải tăng success count thay vì tạo bản ghi rác.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Cleanup thư mục tạm không được làm acceptance fail.
            }
        }
    }

    private static void CheckProcedureGraphPartialReuseAndBranching()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "personalai-procedure-graph-acceptance",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var workspace =
                new AcceptanceWorkspaceContextAccessor(
                    PersonalWorkspaceIds.PersonalAi);

            var transitions =
                new SqliteComputerOperatorVerifiedTransitionStore(
                    configuration,
                    workspace);

            var graph =
                new SqliteComputerOperatorProcedureGraphStore(
                    configuration,
                    workspace);

            var session =
                new ComputerOperatorProcedureGraphSession(
                    graph);

            var stateA =
                transitions.FingerprintSemanticState(
                    "observed-state-a");
            var stateB =
                transitions.FingerprintSemanticState(
                    "observed-state-b");
            var stateC =
                transitions.FingerprintSemanticState(
                    "observed-state-c");
            var stateD =
                transitions.FingerprintSemanticState(
                    "observed-state-d");
            var stateX =
                transitions.FingerprintSemanticState(
                    "observed-state-x");

            _ =
                session.ObserveState(
                    stateA);

            session.RecordVerifiedAction(
                stateA,
                "strategy-a-b",
                transitions.FingerprintSemanticState(
                    "effect-b"),
                0.97);

            var edgeAB =
                session.ObserveState(
                    stateB);

            session.RecordVerifiedAction(
                stateB,
                "strategy-b-c",
                transitions.FingerprintSemanticState(
                    "effect-c"),
                0.96);

            var edgeBC =
                session.ObserveState(
                    stateC);

            // C -> D thất bại: không gọi RecordVerifiedAction, nên graph cũ không bị phá.
            var beforeRecovery =
                graph.GetOutgoing(
                    stateC);

            session.RecordVerifiedAction(
                stateC,
                "strategy-c-x",
                transitions.FingerprintSemanticState(
                    "effect-x"),
                0.93);

            var edgeCX =
                session.ObserveState(
                    stateX);

            // Sau này tìm được một strategy khác cũng đúng từ C.
            _ =
                session.ObserveState(
                    stateC);

            session.RecordVerifiedAction(
                stateC,
                "strategy-c-d-v2",
                transitions.FingerprintSemanticState(
                    "effect-d"),
                0.95);

            var edgeCD =
                session.ObserveState(
                    stateD);

            var outgoingA =
                graph.GetOutgoing(
                    stateA);

            var outgoingB =
                graph.GetOutgoing(
                    stateB);

            var outgoingC =
                graph.GetOutgoing(
                    stateC);

            Require(
                edgeAB is not null &&
                edgeBC is not null &&
                edgeCX is not null &&
                edgeCD is not null &&
                beforeRecovery.Count == 0 &&
                outgoingA.Count == 1 &&
                outgoingB.Count == 1 &&
                outgoingC.Count == 2 &&
                outgoingC.Any(item =>
                    item.ToStateFingerprint == stateX &&
                    item.StrategyKey == "strategy-c-x") &&
                outgoingC.Any(item =>
                    item.ToStateFingerprint == stateD &&
                    item.StrategyKey == "strategy-c-d-v2") &&
                graph.CountNodes() == 5 &&
                graph.CountEdges() == 4,
                "Procedure graph phải giữ A->B và B->C sau failure ở C, đồng thời cho phép C có nhiều verified branch mới mà không ghi đè fragment cũ.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Cleanup thư mục tạm không được làm acceptance fail.
            }
        }
    }

    private static void CheckGraphAwarePartialResume()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "personalai-partial-resume-acceptance",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var workspace =
                new AcceptanceWorkspaceContextAccessor(
                    PersonalWorkspaceIds.PersonalAi);

            var transitions =
                new SqliteComputerOperatorVerifiedTransitionStore(
                    configuration,
                    workspace);

            var graph =
                new SqliteComputerOperatorProcedureGraphStore(
                    configuration,
                    workspace);

            var stateA =
                transitions.FingerprintSemanticState(
                    "resume-state-a");
            var stateB =
                transitions.FingerprintSemanticState(
                    "resume-state-b");
            var stateC =
                transitions.FingerprintSemanticState(
                    "resume-state-c");
            var stateX =
                transitions.FingerprintSemanticState(
                    "resume-state-x");

            _ =
                graph.ObserveNode(
                    stateA);
            _ =
                graph.ObserveNode(
                    stateB);
            _ =
                graph.ObserveNode(
                    stateC);

            _ =
                graph.RecordVerifiedEdge(
                    stateA,
                    stateB,
                    "strategy-a-b",
                    transitions.FingerprintSemanticState(
                        "effect-b"),
                    0.97);

            _ =
                graph.RecordVerifiedEdge(
                    stateB,
                    stateC,
                    "strategy-b-c",
                    transitions.FingerprintSemanticState(
                        "effect-c"),
                    0.96);

            var logger =
                Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    SqliteComputerOperatorCheckpointStore>.Instance;

            var checkpointStore =
                new SqliteComputerOperatorCheckpointStore(
                    configuration,
                    workspace,
                    logger);

            var checkpoint =
                checkpointStore.StartOrResume(
                    "resume graph task");

            checkpoint =
                checkpointStore.SaveProgress(
                    checkpoint,
                    new[] { "A done", "B done" },
                    "continue from current state",
                    0.50,
                    lastVerifiedAction: "strategy-a-b",
                    lastVerifiedExpectedEffect: "state b",
                    lastObservedStateFingerprint: stateB);

            // Giả lập process mới: running checkpoint sẽ được phục hồi thành interrupted.
            var afterRestartStore =
                new SqliteComputerOperatorCheckpointStore(
                    configuration,
                    workspace,
                    logger);

            var resumable =
                afterRestartStore.FindResumable(
                    "resume graph task");

            Require(
                resumable is not null &&
                resumable.Status ==
                    ComputerOperatorCheckpointStatuses.Interrupted &&
                resumable.LastObservedStateFingerprint == stateB,
                "Checkpoint sau restart phải giữ state fingerprint đã quan sát và chuyển running thành interrupted.");

            var resolver =
                new ComputerOperatorPartialResumeResolver(
                    graph);

            var exact =
                resolver.Assess(
                    resumable,
                    stateB);

            var joinMiddle =
                resolver.Assess(
                    resumable,
                    stateC);

            var unknown =
                resolver.Assess(
                    resumable,
                    stateX);

            Require(
                exact.Kind ==
                    ComputerOperatorResumeKinds.ExactCheckpointState &&
                exact.ExactCheckpointMatch &&
                !exact.ReplayLastActionAllowed &&
                joinMiddle.Kind ==
                    ComputerOperatorResumeKinds.KnownGraphNode &&
                !joinMiddle.ExactCheckpointMatch &&
                joinMiddle.KnownProcedureNode &&
                joinMiddle.IncomingEdges >= 1 &&
                !joinMiddle.ReplayLastActionAllowed &&
                unknown.Kind ==
                    ComputerOperatorResumeKinds.UnknownObservedState &&
                !unknown.KnownProcedureNode &&
                !unknown.ReplayLastActionAllowed,
                "Resume phải re-observe: exact state tiếp tục tại chỗ; state đã biết join graph giữa chừng; state mới replan; không trường hợp nào replay action cuối.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Cleanup thư mục tạm không được làm acceptance fail.
            }
        }
    }

    private static void CheckProcedureFragmentRetrieval()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "personalai-fragment-retrieval-acceptance",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var workspace =
                new AcceptanceWorkspaceContextAccessor(
                    PersonalWorkspaceIds.PersonalAi);

            var transitions =
                new SqliteComputerOperatorVerifiedTransitionStore(
                    configuration,
                    workspace);

            var graph =
                new SqliteComputerOperatorProcedureGraphStore(
                    configuration,
                    workspace);

            string State(string value) =>
                transitions.FingerprintSemanticState(value);

            var stateA = State("fragment-a");
            var stateB = State("fragment-b");
            var stateC = State("fragment-c");
            var stateD = State("fragment-d");
            var stateX = State("fragment-x");

            _ = graph.RecordVerifiedEdge(
                stateA,
                stateB,
                "strategy-a-b",
                State("effect-b"),
                0.98,
                "click-left");

            _ = graph.RecordVerifiedEdge(
                stateB,
                stateC,
                "strategy-b-c",
                State("effect-c"),
                0.97,
                "press-key");

            _ = graph.RecordVerifiedEdge(
                stateC,
                stateD,
                "strategy-c-d",
                State("effect-d"),
                0.96,
                "click-left");

            // Nhánh phụ kém tin cậy hơn.
            _ = graph.RecordVerifiedEdge(
                stateB,
                stateX,
                "strategy-b-x",
                State("effect-x"),
                0.82,
                "scroll");

            // Tạo cycle để chắc chắn retriever không quay vô hạn.
            _ = graph.RecordVerifiedEdge(
                stateD,
                stateB,
                "strategy-d-b",
                State("effect-b-again"),
                0.90,
                "press-hotkey");

            var retriever =
                new ComputerOperatorProcedureFragmentRetriever(
                    graph);

            var fragments =
                retriever.Retrieve(
                    stateB,
                    maximumDepth: 6,
                    maximumFragments: 8);

            Require(
                fragments.Count > 0 &&
                fragments.Count <= 8 &&
                fragments.All(fragment =>
                    fragment.Length <=
                        ComputerOperatorProcedureFragmentRetriever.MaximumDepthLimit) &&
                fragments.All(fragment =>
                    fragment.Edges
                        .Select(edge => edge.FromStateFingerprint)
                        .Append(fragment.EndStateFingerprint)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count() ==
                    fragment.Length + 1) &&
                fragments.Any(fragment =>
                    fragment.EndStateFingerprint == stateD &&
                    fragment.Edges.Count == 2 &&
                    fragment.Edges[0].ActionKind == "press-key" &&
                    fragment.Edges[1].ActionKind == "click-left") &&
                fragments[0].BottleneckConfidence >= 0.96,
                "Fragment Retrieval phải tìm được B->C->D đã VERIFY PASS, ưu tiên reliability và không lặp state khi graph có cycle D->B.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Cleanup thư mục tạm không được làm acceptance fail.
            }
        }
    }

    private static void CheckStrategyRankerFastPathGate()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "personalai-fast-path-acceptance",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var workspace =
                new AcceptanceWorkspaceContextAccessor(
                    PersonalWorkspaceIds.PersonalAi);

            var transitions =
                new SqliteComputerOperatorVerifiedTransitionStore(
                    configuration,
                    workspace);

            var graph =
                new SqliteComputerOperatorProcedureGraphStore(
                    configuration,
                    workspace);

            var state =
                transitions.FingerprintSemanticState(
                    "fast-path-state");

            var next =
                transitions.FingerprintSemanticState(
                    "fast-path-next");

            var effect =
                transitions.FingerprintSemanticState(
                    "fast-path-effect");

            for (var index = 0;
                 index <
                    ComputerOperatorStrategyRanker.MinimumFastPathSuccesses;
                 index++)
            {
                _ =
                    graph.RecordVerifiedEdge(
                        state,
                        next,
                        "abc123strategy",
                        effect,
                        0.98,
                        "click-left");
            }

            var contextService =
                new ComputerOperatorProcedureContextService();

            var contextStore =
                new SqliteComputerOperatorProcedureContextStore(
                    configuration,
                    workspace);

            var lifecycle =
                new ComputerOperatorProcedureEdgeLifecycleService(
                    configuration,
                    workspace,
                    graph);

            var ranker =
                new ComputerOperatorStrategyRanker(
                    graph,
                    contextStore,
                    contextService,
                    lifecycle);

            var eligible =
                ranker.AssessFastPath(
                    state,
                    "abc123strategy",
                    "click-left");

            var mismatch =
                ranker.AssessFastPath(
                    state,
                    "different-strategy",
                    "click-left");

            var unsafeAction =
                ranker.AssessFastPath(
                    state,
                    "abc123strategy",
                    "type-text");

            var weakState =
                transitions.FingerprintSemanticState(
                    "fast-path-weak");

            _ =
                graph.RecordVerifiedEdge(
                    weakState,
                    next,
                    "weak-strategy",
                    effect,
                    0.99,
                    "click-left");

            var weak =
                ranker.AssessFastPath(
                    weakState,
                    "weak-strategy",
                    "click-left");

            Require(
                eligible.Eligible &&
                eligible.Status ==
                    ComputerOperatorFastPathStatuses.Eligible &&
                eligible.SuccessCount >=
                    ComputerOperatorStrategyRanker.MinimumFastPathSuccesses &&
                eligible.Confidence >=
                    ComputerOperatorStrategyRanker.MinimumFastPathConfidence &&
                !mismatch.Eligible &&
                mismatch.Status ==
                    ComputerOperatorFastPathStatuses.StrategyMismatch &&
                !unsafeAction.Eligible &&
                unsafeAction.Status ==
                    ComputerOperatorFastPathStatuses.ActionNotEligible &&
                !weak.Eligible &&
                weak.Status ==
                    ComputerOperatorFastPathStatuses.NotEnoughEvidence,
                "Fast Path chỉ được bật khi exact-state strategy/action có đủ verified successes và confidence; mismatch, action ngoài allowlist hoặc bằng chứng yếu phải quay về planner thường.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Cleanup thư mục tạm không được làm acceptance fail.
            }
        }
    }

    private static void CheckProcedureVersioningAndContextCompatibility()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "personalai-procedure-versioning-acceptance",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var workspace =
                new AcceptanceWorkspaceContextAccessor(
                    PersonalWorkspaceIds.PersonalAi);

            var transitions =
                new SqliteComputerOperatorVerifiedTransitionStore(
                    configuration,
                    workspace);

            var graph =
                new SqliteComputerOperatorProcedureGraphStore(
                    configuration,
                    workspace);

            var contexts =
                new SqliteComputerOperatorProcedureContextStore(
                    configuration,
                    workspace);

            var compatibility =
                new ComputerOperatorProcedureContextService();

            var fragments =
                new ComputerOperatorProcedureFragmentRetriever(
                    graph);

            var versioned =
                new ComputerOperatorVersionedFragmentRetriever(
                    fragments,
                    contexts,
                    compatibility);

            string State(string value) =>
                transitions.FingerprintSemanticState(value);

            var oldState =
                State("version-old-state");
            var oldNext =
                State("version-old-next");
            var newState =
                State("version-new-state");

            var family =
                State("same-app-family");
            var version1 =
                State("ui-version-1");
            var version2 =
                State("ui-version-2");

            var oldContext =
                new ComputerOperatorProcedureContextDescriptor(
                    family,
                    version1);

            var currentContext =
                new ComputerOperatorProcedureContextDescriptor(
                    family,
                    version2);

            _ =
                contexts.Observe(
                    oldState,
                    oldContext);

            _ =
                contexts.Observe(
                    newState,
                    currentContext);

            for (var index = 0;
                 index < 3;
                 index++)
            {
                _ =
                    graph.RecordVerifiedEdge(
                        oldState,
                        oldNext,
                        "old-version-strategy",
                        State("old-version-effect"),
                        0.98,
                        "structured-invoke");
            }

            var remembered =
                contexts.Get(
                    oldState)
                ?? throw new InvalidOperationException(
                    "Không đọc được old version context.");

            var assessment =
                compatibility.Assess(
                    currentContext,
                    remembered);

            var candidates =
                versioned.Retrieve(
                    newState,
                    currentContext,
                    maximumDepth: 4,
                    maximumCandidates: 6);

            var oldCandidate =
                candidates.FirstOrDefault(item =>
                    item.SourceStateFingerprint == oldState);

            Require(
                assessment.Kind ==
                    ComputerOperatorContextCompatibilityKinds.SameFamily &&
                assessment.Compatible &&
                !assessment.ExactVersion &&
                assessment.ConfidenceMultiplier < 1.0 &&
                oldCandidate is not null &&
                !oldCandidate.ExactStartState &&
                oldCandidate.CompatibilityKind ==
                    ComputerOperatorContextCompatibilityKinds.SameFamily &&
                oldCandidate.EffectiveConfidence <
                    oldCandidate.Fragment.BottleneckConfidence &&
                oldCandidate.Fragment.Edges[0].ActionKind ==
                    "structured-invoke",
                "UI version mới cùng family phải tìm lại được fragment cũ với confidence bị hạ; fragment version cũ không được coi là exact-state Fast Path.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Cleanup thư mục tạm không được làm acceptance fail.
            }
        }
    }

    private static void CheckProcedureDecayAndSupersession()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "personalai-procedure-decay-supersession-acceptance",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var workspace =
                new AcceptanceWorkspaceContextAccessor(
                    PersonalWorkspaceIds.PersonalAi);

            var transitions =
                new SqliteComputerOperatorVerifiedTransitionStore(
                    configuration,
                    workspace);

            var graph =
                new SqliteComputerOperatorProcedureGraphStore(
                    configuration,
                    workspace);

            var lifecycle =
                new ComputerOperatorProcedureEdgeLifecycleService(
                    configuration,
                    workspace,
                    graph);

            string State(string value) =>
                transitions.FingerprintSemanticState(value);

            var from =
                State("supersession-from");
            var to =
                State("supersession-to");
            var effect =
                State("supersession-effect");

            ComputerOperatorProcedureEdge oldEdge =
                graph.RecordVerifiedEdge(
                    from,
                    to,
                    "old-strategy",
                    effect,
                    0.90,
                    "structured-invoke");

            oldEdge =
                graph.RecordVerifiedEdge(
                    from,
                    to,
                    "old-strategy",
                    effect,
                    0.90,
                    "structured-invoke");

            oldEdge =
                graph.RecordVerifiedEdge(
                    from,
                    to,
                    "old-strategy",
                    effect,
                    0.90,
                    "structured-invoke");

            ComputerOperatorProcedureEdge newEdge =
                graph.RecordVerifiedEdge(
                    from,
                    to,
                    "new-strategy",
                    effect,
                    0.98,
                    "structured-invoke");

            for (var index = 0; index < 4; index++)
            {
                newEdge =
                    graph.RecordVerifiedEdge(
                        from,
                        to,
                        "new-strategy",
                        effect,
                        0.98,
                        "structured-invoke");
            }

            var fresh =
                lifecycle.Evaluate(
                    newEdge,
                    newEdge.LastVerifiedAt.AddDays(10));

            var aging =
                lifecycle.Evaluate(
                    newEdge,
                    newEdge.LastVerifiedAt.AddDays(120));

            var stale =
                lifecycle.Evaluate(
                    newEdge,
                    newEdge.LastVerifiedAt.AddDays(220));

            var reconciliation =
                lifecycle.Reconcile(
                    from);

            var oldLifecycle =
                lifecycle.Evaluate(
                    oldEdge);

            var newLifecycle =
                lifecycle.Evaluate(
                    newEdge);

            var allEdges =
                graph.GetOutgoing(
                    from,
                    limit: 10);

            var activeFragments =
                new ComputerOperatorProcedureFragmentRetriever(
                    graph,
                    lifecycle)
                    .Retrieve(
                        from,
                        maximumDepth: 3,
                        maximumFragments: 6);

            Require(
                fresh.DecayMultiplier == 1.0 &&
                aging.DecayMultiplier < fresh.DecayMultiplier &&
                stale.DecayMultiplier < aging.DecayMultiplier &&
                reconciliation.SupersededEdges >= 1 &&
                oldLifecycle.Status ==
                    ComputerOperatorProcedureEdgeStatuses.Superseded &&
                oldLifecycle.EffectiveConfidence == 0 &&
                oldLifecycle.SupersededByStrategyKey ==
                    "new-strategy" &&
                newLifecycle.Status ==
                    ComputerOperatorProcedureEdgeStatuses.Active &&
                allEdges.Count == 2 &&
                allEdges.Any(edge =>
                    edge.StrategyKey == "old-strategy") &&
                allEdges.Any(edge =>
                    edge.StrategyKey == "new-strategy") &&
                activeFragments.Count > 0 &&
                activeFragments.All(fragment =>
                    fragment.Edges.All(edge =>
                        edge.StrategyKey != "old-strategy")),
                "Decay phải giảm effective confidence theo tuổi; strategy mới mạnh hơn phải supersede strategy cũ, nhưng edge cũ vẫn tồn tại trong graph để audit và bị loại khỏi active fragment retrieval.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Cleanup thư mục tạm không được làm acceptance fail.
            }
        }
    }

    private static void CheckRecoveryCoordinatorPriority()
    {
        var coordinator =
            new ComputerOperatorRecoveryCoordinator();

        var runtimeProgressing =
            coordinator.Decide(
                new ComputerOperatorRecoveryCoordinatorInput(
                    RuntimeState:
                        new ComputerOperatorRuntimeStateAssessment(
                            ComputerOperatorRuntimeStates.Progressing,
                            ComputerOperatorRuntimeDecisions.Wait,
                            0.92,
                            Terminal: false,
                            "Đang tiến triển."),
                    RecoveryPlan:
                        new ComputerOperatorRecoveryPlan(
                            ComputerOperatorRecoveryAction.Replan,
                            Array.Empty<ComputerOperatorRecoveryAction>(),
                            AllowSameStrategyRetry: false,
                            DelayMs: 0,
                            "Recovery muốn replan.")));

        var loopWithFragment =
            coordinator.Decide(
                new ComputerOperatorRecoveryCoordinatorInput(
                    LoopAssessment:
                        new ComputerOperatorLoopAssessment(
                            Detected: true,
                            RequiresStrategyChange: true,
                            Kind: "repeated-strategy",
                            Detail: "Lặp strategy.",
                            Occurrences: 3),
                    CompatibleFragmentAvailable: true,
                    ExactFragmentAvailable: true));

        var superseded =
            coordinator.Decide(
                new ComputerOperatorRecoveryCoordinatorInput(
                    CurrentStrategySuperseded: true,
                    CompatibleFragmentAvailable: false,
                    ExactFragmentAvailable: false));

        var repeatedFailureWithFragment =
            coordinator.Decide(
                new ComputerOperatorRecoveryCoordinatorInput(
                    RecoveryPlan:
                        new ComputerOperatorRecoveryPlan(
                            ComputerOperatorRecoveryAction.Reobserve,
                            Array.Empty<ComputerOperatorRecoveryAction>(),
                            AllowSameStrategyRetry: false,
                            DelayMs: 0,
                            "Quan sát lại."),
                    RepeatedFailures: 3,
                    CompatibleFragmentAvailable: true));

        var loadingWait =
            coordinator.Decide(
                new ComputerOperatorRecoveryCoordinatorInput(
                    RecoveryPlan:
                        new ComputerOperatorRecoveryPlan(
                            ComputerOperatorRecoveryAction.WaitForStableUi,
                            new[]
                            {
                                ComputerOperatorRecoveryAction.Reobserve
                            },
                            AllowSameStrategyRetry: true,
                            DelayMs: 700,
                            "UI vẫn có thể đang tải.")));

        Require(
            runtimeProgressing.Decision ==
                ComputerOperatorRecoveryCoordinatorDecisions.Wait &&
            !runtimeProgressing.AllowSameStrategyRetry &&
            loopWithFragment.Decision ==
                ComputerOperatorRecoveryCoordinatorDecisions.ResumeFragment &&
            loopWithFragment.PreferKnownFragment &&
            loopWithFragment.RequiresFreshObservation &&
            superseded.Decision ==
                ComputerOperatorRecoveryCoordinatorDecisions.ChangeStrategy &&
            !superseded.AllowSameStrategyRetry &&
            repeatedFailureWithFragment.Decision ==
                ComputerOperatorRecoveryCoordinatorDecisions.ResumeFragment &&
            loadingWait.Decision ==
                ComputerOperatorRecoveryCoordinatorDecisions.Wait &&
            loadingWait.AllowSameStrategyRetry,
            "Recovery Coordinator phải ưu tiên runtime progress -> wait, loop/repeated failure + fragment -> resume-fragment, superseded -> change-strategy và loading -> wait.");
    }

    private static void CheckMcpCapabilityBoundary()
    {
        var constructor =
            typeof(PersonalAiMcpCapabilityBridge)
                .GetConstructors()
                .Single();

        var dependencies =
            constructor
                .GetParameters()
                .Select(parameter =>
                    parameter.ParameterType)
                .ToArray();

        var local =
            new DefaultHttpContext();

        local.Connection.RemoteIpAddress =
            System.Net.IPAddress.Loopback;

        local.Request.Host =
            new HostString(
                "localhost",
                5000);

        var remote =
            new DefaultHttpContext();

        remote.Connection.RemoteIpAddress =
            System.Net.IPAddress.Parse(
                "10.20.30.40");

        remote.Request.Host =
            new HostString(
                "localhost",
                5000);

        var rebinding =
            new DefaultHttpContext();

        rebinding.Connection.RemoteIpAddress =
            System.Net.IPAddress.Loopback;

        rebinding.Request.Host =
            new HostString(
                "attacker.example",
                5000);

        Require(
            dependencies.Length == 2 &&
            dependencies.Contains(
                typeof(ICapabilityToolRouter)) &&
            dependencies.Contains(
                typeof(IExecutionGateway)) &&
            !dependencies.Contains(
                typeof(IComputerUseService)) &&
            !dependencies.Contains(
                typeof(IComputerOperatorTaskService)) &&
            !dependencies.Contains(
                typeof(IComputerOperatorActionExecutor)) &&
            McpIntegrationEndpoints.IsLoopbackRequest(
                local) &&
            !McpIntegrationEndpoints.IsLoopbackRequest(
                remote) &&
            !McpIntegrationEndpoints.IsLoopbackRequest(
                rebinding),
            "MCP boundary phải chỉ phụ thuộc CapabilityToolRouter + ExecutionGateway, không gọi ComputerUse/Operator executor trực tiếp; HTTP MCP phải chặn remote IP và Host không phải loopback.");
    }

    private static void CheckGeminiPollyResilienceBoundary()
    {
        Require(
            GeminiHttpResiliencePolicy.RequestTimeout >= TimeSpan.FromSeconds(20) &&
            GeminiHttpResiliencePolicy.RequestTimeout <= TimeSpan.FromSeconds(90) &&
            GeminiHttpResiliencePolicy.BreakDuration >= TimeSpan.FromSeconds(10) &&
            GeminiHttpResiliencePolicy.MinimumThroughput >= 4 &&
            GeminiHttpResiliencePolicy.FailureRatio is >= 0.25d and <= 0.75d &&
            GeminiHttpResiliencePolicy.IsolatedLaneCountForAcceptance == 3 &&
            Enum.GetValues<GeminiResilienceLane>().Length == 3 &&
            GeminiHttpResiliencePolicy.IsTransientStatusCode(System.Net.HttpStatusCode.RequestTimeout) &&
            GeminiHttpResiliencePolicy.IsTransientStatusCode(System.Net.HttpStatusCode.TooManyRequests) &&
            GeminiHttpResiliencePolicy.IsTransientStatusCode(System.Net.HttpStatusCode.ServiceUnavailable) &&
            !GeminiHttpResiliencePolicy.IsTransientStatusCode(System.Net.HttpStatusCode.BadRequest),
            "Gemini resilience phải có timeout hữu hạn, circuit breaker đủ bằng chứng, cô lập Reply/FunctionPlanning/FunctionContinuation thành ba lane và chỉ coi 408/429/5xx là lỗi tạm thời.");
    }



    private static void CheckGeminiResilienceLaneIsolation()
    {
        Require(
            GeminiChatService.ReplyResilienceLane ==
                GeminiResilienceLane.Reply &&
            GeminiChatService.FunctionPlanningResilienceLane ==
                GeminiResilienceLane.FunctionPlanning &&
            GeminiChatService.FunctionContinuationResilienceLane ==
                GeminiResilienceLane.FunctionContinuation &&
            GeminiChatService.ReplyResilienceLane !=
                GeminiChatService.FunctionPlanningResilienceLane &&
            GeminiChatService.FunctionPlanningResilienceLane !=
                GeminiChatService.FunctionContinuationResilienceLane &&
            GeminiChatService.ReplyResilienceLane !=
                GeminiChatService.FunctionContinuationResilienceLane,
            "GeminiChatService phải đưa reply, function planning và continuation vào ba resilience lane độc lập; acceptance không được phụ thuộc đường dẫn source trên runner.");
    }


    private static void CheckNativeToolIntentArbitration()
    {
        var chatConstructor =
            typeof(ChatTurnService)
                .GetConstructors()
                .Single();

        var chatDependencies =
            chatConstructor
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
                .ToArray();

        var directLeagueShortcut =
            typeof(ChatTurnService)
                .GetMethod(
                    "IsDirectLeaguePracticeCommand",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static);

        var keywordOperatorRouter =
            typeof(ToolOrchestrationService)
                .GetMethod(
                    "TryGetComputerOperatorGoal",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static);

        var nativePlanner =
            typeof(IAiProvider)
                .GetMethod(
                    "ProposeFunctionCallAsync");

        Require(
            !chatDependencies.Contains(typeof(IToolExecutionService)) &&
            directLeagueShortcut is null &&
            keywordOperatorRouter is null &&
            nativePlanner is not null,
            "Chat không được tự execute app-specific tool hoặc route Computer Operator bằng keyword; tool selection phải đi qua provider-native Function Calling rồi mới qua proposal/safety gate.");
    }



    private static void CheckFunctionPlannerActionPreference()
    {
        static string ReadPlanningInstructions(Type providerType)
        {
            var field =
                providerType.GetField(
                    "FunctionPlanningInstructions",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static);

            return field?.GetRawConstantValue() as string ?? string.Empty;
        }

        var gemini = ReadPlanningInstructions(typeof(GeminiChatService));
        var openAi = ReadPlanningInstructions(typeof(OpenAiChatService));

        var expectedActionPreference =
            "If the latest user message explicitly asks the application to perform an action";

        var expectedSafetyBoundary =
            "confirmation and execution are handled separately by the application safety layer";

        Require(
            gemini.Contains(expectedActionPreference, StringComparison.Ordinal) &&
            openAi.Contains(expectedActionPreference, StringComparison.Ordinal) &&
            gemini.Contains(expectedSafetyBoundary, StringComparison.Ordinal) &&
            openAi.Contains(expectedSafetyBoundary, StringComparison.Ordinal),
            "Function planner phải ưu tiên đề xuất capability cho yêu cầu hành động rõ ràng, nhưng model không được tự coi proposal là execution hoặc bypass safety/confirmation.");
    }



    private static void CheckFunctionPlannerIgnoresStaleAssistantCapabilityClaims()
    {
        var method =
            typeof(ToolOrchestrationService)
                .GetMethod(
                    "BuildFunctionPlanningConversation",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static);

        Require(
            method is not null,
            "Tool selection phải có conversation builder riêng để không dùng nguyên assistant history cũ.");

        var messages = new[]
        {
            new ChatMessage("user", "Mở Notepad."),
            new ChatMessage("assistant", "Tôi không có quyền điều khiển máy tính."),
            new ChatMessage(
                "user",
                "Mở League of Legends. Chờ ứng dụng khởi động hoàn toàn, sau đó vào Practice Tool.")
        };

        var planned =
            (IReadOnlyList<ChatMessage>?)method.Invoke(
                null,
                new object[] { messages });

        Require(
            planned is not null &&
            planned.Count == 1 &&
            planned[0].Role.Equals(
                "user",
                StringComparison.OrdinalIgnoreCase) &&
            planned[0].Content.Contains(
                "League of Legends",
                StringComparison.OrdinalIgnoreCase) &&
            !planned[0].Content.Contains(
                "Notepad",
                StringComparison.OrdinalIgnoreCase) &&
            !planned[0].Content.Contains(
                "không có quyền",
                StringComparison.OrdinalIgnoreCase),
            "Function planner phải bind tool/arguments vào đúng yêu cầu user mới nhất; lệnh cũ như Notepad và assistant history không được rò sang proposal hiện tại.");
    }




    private void CheckToolRegistryUsesGeneralComputerOperator()
    {
        var definitions =
            toolRegistry.GetAll();

        Require(
            definitions.Any(definition =>
                definition.Name.Equals(
                    "computer.operator.run-task",
                    StringComparison.OrdinalIgnoreCase)) &&
            !definitions.Any(definition =>
                definition.Name.Equals(
                    "league.practice.open",
                    StringComparison.OrdinalIgnoreCase)),
            "Tool Registry phải expose Computer Operator tổng quát và không được expose league.practice.open cho provider-native function planning.");
    }


    private static void CheckOperatorConsoleIgnoresNonClickButtonNotifications()
    {
        var method =
            typeof(WindowsAiOperatorConsoleService)
                .GetMethod(
                    "IsButtonClickCommand",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static);

        Require(
            method is not null,
            "Operator Console phải có bộ lọc riêng cho Win32 button notification.");

        var clicked =
            (bool?)method.Invoke(
                null,
                new object[] { 0, new IntPtr(123) });

        var focusNotification =
            (bool?)method.Invoke(
                null,
                new object[] { 6, new IntPtr(123) });

        var menuStyleCommand =
            (bool?)method.Invoke(
                null,
                new object[] { 0, IntPtr.Zero });

        Require(
            clicked == true &&
            focusNotification == false &&
            menuStyleCommand == false,
            "Chỉ BN_CLICKED từ control handle thật mới được phép Pause/Resume/Stop; focus/menu notification không được dừng Operator.");
    }



    private static void CheckNestedToolJoinsExistingOperatorSession()
    {
        using var execution = new ComputerOperatorExecutionControl();

        var parentToken =
            execution.Begin(
                "parent-operator-task",
                pausable: true);

        var joined =
            execution.TryJoinRunning(
                out var childToken);

        Require(
            joined &&
            !parentToken.IsCancellationRequested &&
            !childToken.IsCancellationRequested &&
            parentToken == childToken &&
            execution.Running,
            "Primitive con phải dùng cùng token/session với task cha; không được Begin() mới làm hủy parent token.");

        execution.Complete();

        Require(
            !execution.Running &&
            !parentToken.IsCancellationRequested,
            "Complete session không được biến một task đã hoàn tất bình thường thành cancellation.");
    }



    private static void CheckAppLaunchWrapperDoesNotOwnOuterOperatorSession()
    {
        var wrapperMethod =
            typeof(ToolExecutionService)
                .GetMethod(
                    "IsComputerOperatorWrapper",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static);

        var trackingMethod =
            typeof(ToolExecutionService)
                .GetMethod(
                    "ShouldTrackInOperatorConsole",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static);

        Require(
            wrapperMethod is not null &&
            trackingMethod is not null,
            "ToolExecutionService phải phân biệt wrapper Computer Operator với primitive desktop.");

        var appLaunchIsWrapper =
            (bool?)wrapperMethod.Invoke(
                null,
                new object[] { "computer.app.launch" });

        var runTaskIsWrapper =
            (bool?)wrapperMethod.Invoke(
                null,
                new object[] { "computer.operator.run-task" });

        var appLaunchTrackedOutside =
            (bool?)trackingMethod.Invoke(
                null,
                new object[] { "computer.app.launch" });

        var primitiveTracked =
            (bool?)trackingMethod.Invoke(
                null,
                new object[] { "computer.keyboard.press-key" });

        Require(
            appLaunchIsWrapper == true &&
            runTaskIsWrapper == true &&
            appLaunchTrackedOutside == false &&
            primitiveTracked == true,
            "computer.app.launch/run-task chỉ chuyển quyền vào ComputerOperatorTaskService; primitive desktop mới được join/own execution session ở ToolExecutionService.");
    }


    private static void CheckOcrProviderEmptyResultDoesNotTripHealth()
    {
        var health =
            new LocalVisualProviderHealthRegistry();

        var empty =
            new AcceptanceOcrProvider(
                "windows-ocr",
                new DesktopOcrObservation(
                    Available: true,
                    Text: string.Empty,
                    Language: "en-US",
                    Lines:
                        Array.Empty<DesktopOcrLine>(),
                    CaptureWidth: 800,
                    CaptureHeight: 600,
                    Provider: "windows-ocr",
                    Reason: "healthy empty"));

        var fallback =
            new AcceptanceOcrProvider(
                "paddleocr-onnx",
                new DesktopOcrObservation(
                    Available: false,
                    Text: string.Empty,
                    Language: string.Empty,
                    Lines:
                        Array.Empty<DesktopOcrLine>(),
                    CaptureWidth: 0,
                    CaptureHeight: 0,
                    Provider: "paddleocr-onnx",
                    Reason: "unavailable"));

        var router =
            new DesktopOcrSensorRouter(
                [empty, fallback],
                health,
                new LocalVisualSensorBudgetPolicy());

        for (var i = 0; i < 4; i++)
        {
            _ =
                router.ReadWindow(
                    "0x1234");
        }

        var snapshot =
            health.Get(
                "windows-ocr");

        Require(
            snapshot.ConsecutiveFailures == 0 &&
            snapshot.State !=
                LocalVisualProviderHealthState.Unavailable,
            "OCR provider healthy nhưng ảnh trống đang bị tính sai thành health failure.");
    }

    private static void CheckSensorBudgetSkipsVisualWhenStructuredResolved()
    {
        var policy =
            new LocalVisualSensorBudgetPolicy();

        var state =
            new ComputerOperatorDesktopState(
                DateTimeOffset.UtcNow,
                ForegroundWindow: null,
                Windows:
                    Array.Empty<ComputerWindowInfo>(),
                FrameLeft: 0,
                FrameTop: 0,
                FrameWidth: 1920,
                FrameHeight: 1080,
                CaptureScope:
                    DesktopCaptureScopes.VirtualDesktop,
                CaptureWindowId: string.Empty,
                CaptureWindowWasForeground: true);

        var budget =
            policy.ForPlanning(
                state,
                structuredTargetResolved: true,
                explicitTextIntent: true);

        Require(
            budget.MaximumTotalMilliseconds == 0 &&
            !budget.AllowWindowsOcr &&
            !budget.AllowPaddleOcr &&
            !budget.AllowOpenCv,
            "Sensor budget vẫn tiêu visual khi structured target đã rõ.");
    }

    private static void CheckSensorBudgetAllowsPaddleForWeakStructuredCoverage()
    {
        var policy =
            new LocalVisualSensorBudgetPolicy();

        var graph =
            new UnifiedStructuredSceneGraph(
                WindowId: "0x1234",
                WindowTitle: "Acceptance",
                ProcessName: "acceptance",
                RootId: "root",
                CapturedAtUtc:
                    DateTimeOffset.UtcNow,
                Nodes:
                [
                    new UnifiedStructuredSceneNode(
                        Id: "root",
                        ParentId: string.Empty,
                        Depth: 0,
                        Role: "Window",
                        Name: "Acceptance",
                        AutomationId: string.Empty,
                        ClassName: "Window",
                        IsEnabled: true,
                        IsFocused: true,
                        IsVisible: true,
                        DesktopLeft: 0,
                        DesktopTop: 0,
                        Width: 800,
                        Height: 600,
                        FrameLeft: 0,
                        FrameTop: 0,
                        Capabilities:
                            Array.Empty<string>(),
                        Children:
                            Array.Empty<string>())
                ],
                Source: "acceptance",
                Detail: "acceptance");

        var state =
            new ComputerOperatorDesktopState(
                DateTimeOffset.UtcNow,
                ForegroundWindow: null,
                Windows:
                    Array.Empty<ComputerWindowInfo>(),
                FrameLeft: 0,
                FrameTop: 0,
                FrameWidth: 800,
                FrameHeight: 600,
                CaptureScope:
                    DesktopCaptureScopes.VirtualDesktop,
                CaptureWindowId: string.Empty,
                CaptureWindowWasForeground: true,
                StructuredGraph: graph);

        var budget =
            policy.ForPlanning(
                state,
                structuredTargetResolved: false,
                explicitTextIntent: true);

        Require(
            budget.AllowWindowsOcr &&
            budget.AllowPaddleOcr &&
            budget.MaximumOcrMilliseconds >= 3000 &&
            budget.MaximumTotalMilliseconds >= 4000,
            "Sensor budget chưa mở PaddleOCR khi intent text rõ và structured coverage yếu.");
    }

    private static void CheckSensorBudgetRejectsHistoricallySlowProvider()
    {
        var policy =
            new LocalVisualSensorBudgetPolicy();

        var budget =
            new LocalVisualSensorBudget(
                MaximumTotalMilliseconds: 3000,
                MaximumOcrMilliseconds: 1000,
                MaximumTemplateMilliseconds: 500,
                AllowWindowsOcr: true,
                AllowPaddleOcr: true,
                AllowOpenCv: true,
                Reason: "acceptance");

        var health =
            new LocalVisualProviderHealth(
                Provider: "paddleocr-onnx",
                State:
                    LocalVisualProviderHealthState.Healthy,
                ConsecutiveFailures: 0,
                LastLatencyMilliseconds: 2500,
                AverageLatencyMilliseconds: 2500,
                UpdatedAtUtc:
                    DateTimeOffset.UtcNow,
                Reason: "acceptance");

        var allowed =
            policy.CanUseProvider(
                "paddleocr-onnx",
                budget,
                elapsedMilliseconds: 100,
                health,
                out _);

        Require(
            !allowed,
            "Sensor budget vẫn cho provider có latency lịch sử cao hơn gấp đôi budget.");
    }

    private static void CheckProviderHealthUsesLatencyEwma()
    {
        var health =
            new LocalVisualProviderHealthRegistry();

        health.RecordSuccess(
            "windows-ocr",
            100,
            "fast");
        health.RecordSuccess(
            "windows-ocr",
            100,
            "fast");
        health.RecordSuccess(
            "windows-ocr",
            2100,
            "single spike");

        var snapshot =
            health.Get(
                "windows-ocr");

        Require(
            snapshot.AverageLatencyMilliseconds <
                snapshot.LastLatencyMilliseconds &&
            snapshot.AverageLatencyMilliseconds >
                100,
            "Provider health chưa dùng EWMA để làm mượt latency.");
    }

    private static void CheckOpenCvTemplateSensorPolicy()
    {
        var scales =
            OpenCvTemplateMatchingSensor
                .CandidateScalesForAcceptance();

        Require(
            scales.Count == 7 &&
            Math.Abs(scales[0] - 1.0) < 0.001 &&
            scales.Min() >= 0.85 &&
            scales.Max() <= 1.15 &&
            OpenCvTemplateMatchingSensor.AcceptScore(
                0.90,
                0.88) &&
            !OpenCvTemplateMatchingSensor.AcceptScore(
                0.80,
                0.88),
            "OpenCV template sensor policy chưa giữ scale/threshold an toàn.");
    }

    private static void CheckVisualTargetPersistenceTemplateBounds()
    {
        Require(
            DesktopVisualTargetPersistenceService.IsSafeTemplateBox(
                1920,
                1080,
                100,
                100,
                80,
                40) &&
            !DesktopVisualTargetPersistenceService.IsSafeTemplateBox(
                1920,
                1080,
                -1,
                100,
                80,
                40) &&
            !DesktopVisualTargetPersistenceService.IsSafeTemplateBox(
                1920,
                1080,
                1900,
                100,
                80,
                40) &&
            !DesktopVisualTargetPersistenceService.IsSafeTemplateBox(
                1920,
                1080,
                100,
                100,
                600,
                40),
            "Visual target persistence chưa chặn ROI ngoài frame/quá lớn.");
    }

    private static void CheckLocalVisualFusionAgreesOnSameTarget()
    {
        var resolver =
            new LocalVisualTargetResolver();

        var ocr =
            new DesktopOcrResolution(
                DesktopOcrResolutionStatus.Resolved,
                new DesktopOcrTarget(
                    "Continue",
                    100,
                    100,
                    80,
                    30,
                    120,
                    "phrase"),
                Array.Empty<DesktopOcrTarget>(),
                "acceptance");

        var template =
            new DesktopVisualTargetRelocation(
                Available: true,
                Relocated: true,
                Ambiguous: false,
                Confidence: 0.94,
                Left: 104,
                Top: 102,
                Width: 78,
                Height: 30,
                Scale: 1.0,
                Provider: "opencv-template",
                Reason: "acceptance");

        var result =
            resolver.Resolve(
                ocr,
                template);

        Require(
            result.Status ==
                LocalVisualTargetStatus.Resolved &&
            result.Resolved &&
            result.Confidence >= 0.94 &&
            result.Evidence.Count == 2,
            "Local visual fusion chưa tăng confidence khi OCR/OpenCV đồng thuận.");
    }

    private static void CheckLocalVisualFusionRejectsConflictingTargets()
    {
        var resolver =
            new LocalVisualTargetResolver();

        var ocr =
            new DesktopOcrResolution(
                DesktopOcrResolutionStatus.Resolved,
                new DesktopOcrTarget(
                    "Continue",
                    100,
                    100,
                    80,
                    30,
                    120,
                    "phrase"),
                Array.Empty<DesktopOcrTarget>(),
                "acceptance");

        var template =
            new DesktopVisualTargetRelocation(
                Available: true,
                Relocated: true,
                Ambiguous: false,
                Confidence: 0.95,
                Left: 500,
                Top: 300,
                Width: 80,
                Height: 30,
                Scale: 1.0,
                Provider: "opencv-template",
                Reason: "acceptance");

        var result =
            resolver.Resolve(
                ocr,
                template);

        Require(
            result.Status ==
                LocalVisualTargetStatus.Ambiguous &&
            !result.Resolved,
            "Local visual fusion đang chọn bừa khi OCR/OpenCV mạnh nhưng xung đột vị trí.");
    }

    private static void CheckLocalVisualVerificationDetectsChange()
    {
        var service =
            new LocalVisualVerificationService();

        var result =
            service.Evaluate(
                new DesktopLocalVisualObservation(
                    Comparable: true,
                    BeforeHash:
                        new DesktopPerceptualHash(
                            0,
                            "acceptance"),
                    AfterHash:
                        new DesktopPerceptualHash(
                            ulong.MaxValue,
                            "acceptance"),
                    HashDistance: 12,
                    HashSimilarity: 0.8125,
                    RegionDelta:
                        new DesktopFrameDifference(
                            true,
                            0.05,
                            12,
                            100,
                            0,
                            0,
                            100,
                            100,
                            0,
                            "acceptance"),
                    MeaningfulVisualChange: true,
                    Reason: "acceptance"));

        Require(
            result.Status ==
                LocalVisualVerificationStatus.Changed &&
            result.Confidence >= 0.95,
            "Local visual verification chưa xác nhận Changed khi dHash/region đều mạnh.");
    }

    private static void CheckLocalVisualVerificationDetectsStable()
    {
        var service =
            new LocalVisualVerificationService();

        var result =
            service.Evaluate(
                new DesktopLocalVisualObservation(
                    Comparable: true,
                    BeforeHash:
                        new DesktopPerceptualHash(
                            0,
                            "acceptance"),
                    AfterHash:
                        new DesktopPerceptualHash(
                            0,
                            "acceptance"),
                    HashDistance: 0,
                    HashSimilarity: 1.0,
                    RegionDelta:
                        new DesktopFrameDifference(
                            true,
                            0.001,
                            1,
                            100,
                            0,
                            0,
                            0,
                            0,
                            0,
                            "acceptance"),
                    MeaningfulVisualChange: false,
                    Reason: "acceptance"));

        Require(
            result.Status ==
                LocalVisualVerificationStatus.Stable &&
            result.Confidence >= 0.90,
            "Local visual verification chưa xác nhận Stable khi dHash/region đều yên.");
    }

    private static void CheckLocalVisualDifferenceHash()
    {
        var ascending =
            new byte[9 * 8];
        var descending =
            new byte[9 * 8];

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 9; x++)
            {
                ascending[y * 9 + x] =
                    checked((byte)(x * 20));

                descending[y * 9 + x] =
                    checked((byte)((8 - x) * 20));
            }
        }

        var hashAscending =
            DesktopLocalVisualSensor.ComputeDifferenceHash(
                ascending,
                9,
                8);

        var hashAscendingAgain =
            DesktopLocalVisualSensor.ComputeDifferenceHash(
                ascending,
                9,
                8);

        var hashDescending =
            DesktopLocalVisualSensor.ComputeDifferenceHash(
                descending,
                9,
                8);

        Require(
            DesktopLocalVisualSensor.HammingDistance(
                hashAscending,
                hashAscendingAgain) == 0 &&
            DesktopLocalVisualSensor.HammingDistance(
                hashAscending,
                hashDescending) == 64,
            "Local visual dHash chưa ổn định hoặc chưa phân biệt được cấu trúc sáng-tối đối nghịch.");
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

    private static void CheckVerificationRouterLaunchTransitionLocalPass()
    {
        var router =
            new DesktopVerificationRouter();

        var decision =
            BuildClickDecision(
                ComputerCoordinateSpaces.ImagePixel) with
            {
                Action = "click-left",
                TargetLabel = "Demo",
                CurrentSubgoal =
                    "Mở ứng dụng Demo từ kết quả tìm kiếm.",
                ExpectedEffect =
                    "Ứng dụng Demo sẽ bắt đầu khởi chạy.",
                Reason =
                    "Click kết quả tìm kiếm để khởi chạy ứng dụng Demo."
            };

        var matchingObservation =
            new DesktopFastObservation(
                ScreenChanged: true,
                ChangeRatio: 0.14,
                ForegroundWindowChanged: true,
                WindowBoundsChanged: true,
                CursorMoved: true,
                MonitorChanged: false,
                DpiChanged: false,
                TargetMoved: false,
                TargetMissing: false,
                TargetLikelyOccluded: false,
                Summary: "acceptance",
                ActiveProcessName: "demo",
                ActiveWindowTitle: "Demo");

        var mismatchObservation =
            matchingObservation with
            {
                ActiveProcessName = "calculator",
                ActiveWindowTitle = "Calculator"
            };

        var frameDifference =
            new DesktopFrameDifference(
                true,
                0.14,
                140,
                1000,
                0,
                0,
                800,
                600,
                20,
                "acceptance");

        var matchingResult =
            router.Route(
                decision,
                matchingObservation,
                frameDifference);

        var mismatchResult =
            router.Route(
                decision,
                mismatchObservation,
                frameDifference);

        Require(
            matchingResult.Route ==
                DesktopVerificationRoute.LocalVerified &&
            matchingResult.Confidence >= 0.95 &&
            mismatchResult.Route ==
                DesktopVerificationRoute.GeminiRequired &&
            DesktopVerificationRouter.HasExpectedApplicationIdentityMatchForAcceptance(
                decision with
                {
                    TargetLabel = "Notepad",
                    CurrentSubgoal = "Mở Notepad.",
                    ExpectedEffect = "Ứng dụng Notepad được khởi chạy."
                },
                "notepad",
                "Untitled - Notepad") &&
            !DesktopVerificationRouter.HasExpectedApplicationIdentityMatchForAcceptance(
                decision with
                {
                    TargetLabel = "Notepad",
                    CurrentSubgoal = "Mở Notepad.",
                    ExpectedEffect = "Ứng dụng Notepad được khởi chạy."
                },
                "calculator",
                "Calculator"),
            "Launch verification phải local PASS khi app identity khớp và chuyển semantic khi foreground là ứng dụng khác.");
    }

    private static void CheckGeminiPlanningRepairsOnlySafeTruncation()
    {
        var safe =
            """
{"state":"Đang mở","plan":"Chờ","currentSubgoal":"Mở app","goalProgress":0.1,"verifiedMilestones":[],"sceneElements":[],"targetElementId":"","action":"wait","expectedEffect":"","confidence":0.8,"reason":"đang tải"
""";

        var repaired =
            DesktopVisionService.TryRepairTruncatedJsonObjectForAcceptance(
                safe,
                out var repairedJson);

        var unsafeMidString =
            """
{"state":"Đang mở","action":"wait","reason":"chuỗi bị cắt
""";

        var rejected =
            DesktopVisionService.TryRepairTruncatedJsonObjectForAcceptance(
                unsafeMidString,
                out _);

        Require(
            repaired &&
            repairedJson.EndsWith(
                "}",
                StringComparison.Ordinal) &&
            !rejected,
            "Gemini JSON repair đang sửa quá mức hoặc không đóng được envelope truncation an toàn.");
    }

    private static void CheckGeminiMalformedPreviewIsBoundedAndRedacted()
    {
        var preview =
            DesktopVisionService.BuildSafeRawPreviewForAcceptance(
                "{\"state\":\"x\",\"jpegBase64\":\"" +
                new string('A', 1000) +
                "\"}");

        Require(
            preview.Length <= 380 &&
            preview.Contains(
                "<redacted>",
                StringComparison.Ordinal) &&
            !preview.Contains(
                new string('A', 100),
                StringComparison.Ordinal),
            "Gemini malformed preview chưa được giới hạn hoặc chưa redact base64.");
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

    private static void CheckAdaptiveGeminiSkipsKeyboardTransition()
    {
        var policy = new AdaptiveGeminiCallPolicy();
        var decision = BuildClickDecision(
            ComputerCoordinateSpaces.ImagePixel) with
        {
            Action = "press-key",
            Key = "WIN",
            ExpectedEffect = "Windows shell xuất hiện"
        };

        var observation = new DesktopFastObservation(
            ScreenChanged: true,
            ChangeRatio: 0.11,
            ForegroundWindowChanged: true,
            WindowBoundsChanged: true,
            CursorMoved: false,
            MonitorChanged: false,
            DpiChanged: false,
            TargetMoved: false,
            TargetMissing: true,
            TargetLikelyOccluded: true,
            Summary: "acceptance");

        var result = policy.EvaluateVerification(
            decision,
            observation,
            new DesktopFrameDifference(
                true,
                0.11,
                110,
                1000,
                0,
                0,
                500,
                400,
                18,
                "acceptance"));

        Require(
            result.Decision == AdaptiveGeminiDecision.SkipAndPass &&
            result.Confidence >= 0.9,
            "Adaptive Gemini vẫn gọi model dù keyboard transition có foreground + frame evidence mạnh.");
    }

    private static void CheckLocalPlannerUsesGenericWindowsSearch()
    {
        var planner = new DesktopLocalActionPlanner();
        var foreground = new ComputerWindowInfo(
            "0x100",
            "Trình duyệt",
            "msedge",
            100,
            true,
            0,
            0,
            1280,
            720);

        var state = new ComputerOperatorDesktopState(
            DateTimeOffset.UtcNow,
            foreground,
            [foreground],
            0,
            0,
            1920,
            1080,
            DesktopCaptureScopes.VirtualDesktop,
            null,
            false);

        var planned = planner.TryPlan(
            "Mở Calculator, sau đó dừng lại.",
            state,
            string.Empty,
            out var decision);

        Require(
            planned &&
            decision.Action == "press-hotkey" &&
            decision.Keys.SequenceEqual(["WIN", "S"]) &&
            decision.ExpectedEffect.Contains(
                "Windows Search",
                StringComparison.OrdinalIgnoreCase),
            "Local planner không chọn Windows Search generic cho intent mở ứng dụng.");
    }


    private static void CheckLocalPlannerUnderstandsAppLaunchWrapperGoal()
    {
        var planner =
            new DesktopLocalActionPlanner();

        var foreground =
            new ComputerWindowInfo(
                "0x110",
                "Trình duyệt",
                "msedge",
                110,
                true,
                0,
                0,
                1280,
                720);

        var state =
            new ComputerOperatorDesktopState(
                DateTimeOffset.UtcNow,
                foreground,
                [foreground],
                0,
                0,
                1920,
                1080,
                DesktopCaptureScopes.VirtualDesktop,
                null,
                false);

        var planned =
            planner.TryPlan(
                "Đưa ứng dụng “Sample Editor” vào trạng thái đang mở và hiển thị ở foreground. Hãy tự quan sát màn hình hiện tại.",
                state,
                string.Empty,
                out var decision);

        Require(
            planned &&
            decision.Action == "press-hotkey" &&
            decision.Keys.SequenceEqual(["WIN", "S"]) &&
            decision.CurrentSubgoal.Contains(
                "Sample Editor",
                StringComparison.OrdinalIgnoreCase),
            "Local planner phải hiểu semantic wrapper của computer.app.launch thay vì đẩy thẳng sang Gemini/Vision.");
    }

    private static void CheckLocalPlannerUsesEnterForAppLaunchWrapperAfterTyping()
    {
        var planner =
            new DesktopLocalActionPlanner();

        var search =
            new ComputerWindowInfo(
                "0x120",
                "Search",
                "SearchHost",
                120,
                true,
                0,
                0,
                1200,
                900);

        var state =
            new ComputerOperatorDesktopState(
                DateTimeOffset.UtcNow,
                search,
                [search],
                0,
                0,
                1920,
                1080,
                DesktopCaptureScopes.VirtualDesktop,
                null,
                false);

        var history =
            """
STEP 1: VERIFIED press-hotkey|keys=WIN+S|effect=windows search xuất hiện và nhận focus — transition rõ; EXPECTED: Windows Search xuất hiện và nhận focus.
LOCAL-SHELL-TYPED:Sample Editor
""";

        var planned =
            planner.TryPlan(
                "Đưa ứng dụng “Sample Editor” vào trạng thái đang mở và hiển thị ở foreground. Hãy tự quan sát màn hình hiện tại.",
                state,
                history,
                out var decision);

        Require(
            planned &&
            decision.Action == "press-key" &&
            decision.Key.Equals(
                "ENTER",
                StringComparison.OrdinalIgnoreCase) &&
            decision.Reason.Contains(
                "generic",
                StringComparison.OrdinalIgnoreCase),
            "Sau khi Search đã được nhập bằng Text Engine, wrapper app-launch phải ưu tiên Enter deterministic trước OCR/Gemini.");
    }


    private static void CheckLocalPlannerUnderstandsWorkflowOpenGoal()
    {
        var planner = new DesktopLocalActionPlanner();
        var foreground = new ComputerWindowInfo(
            "0x121", "Browser", "msedge", 121, true, 0, 0, 1280, 720);
        var state = new ComputerOperatorDesktopState(
            DateTimeOffset.UtcNow, foreground, [foreground], 0, 0, 1920, 1080,
            DesktopCaptureScopes.VirtualDesktop, null, false);

        var planned = planner.TryPlan(
            "Thực hiện quy trình mở Sample Editor, sau đó vào màn hình tiếp theo.",
            state,
            string.Empty,
            out var decision);

        Require(
            planned &&
            decision.Action == "press-hotkey" &&
            decision.Keys.SequenceEqual(["WIN", "S"]) &&
            decision.CurrentSubgoal.Contains("Sample Editor", StringComparison.OrdinalIgnoreCase),
            "Goal nhiều bước 'thực hiện quy trình mở <app>' phải được hiểu là open-app intent generic.");
    }

    private static void CheckLocalPlannerUsesEnterAfterVerifiedTextEngineSearch()
    {
        var planner = new DesktopLocalActionPlanner();
        var search = new ComputerWindowInfo(
            "0x122", "Search", "SearchHost", 122, true, 0, 0, 1200, 900);
        var state = new ComputerOperatorDesktopState(
            DateTimeOffset.UtcNow, search, [search], 0, 0, 1920, 1080,
            DesktopCaptureScopes.VirtualDesktop, null, false);

        var history =
            """
STEP 1: VERIFIED press-hotkey|keys=WIN+S|effect=windows search xuất hiện và nhận focus — transition rõ.
STEP 2: VERIFIED type-text|coarse=desktop|effect=the search results for 'sample editor' appear in the search window — Local text verifier xác nhận nội dung đúng; EXPECTED: The search results for 'Sample Editor' appear in the search window.
""";

        var planned = planner.TryPlan(
            "Thực hiện quy trình mở Sample Editor, sau đó vào màn hình tiếp theo.",
            state,
            history,
            out var decision);

        Require(
            planned &&
            decision.Action == "press-key" &&
            decision.Key.Equals("ENTER", StringComparison.OrdinalIgnoreCase),
            "Verified type-text trong Search session hiện tại phải chuyển sang Enter deterministic, không phụ thuộc Vision click.");
    }


    private static void CheckLocalPlannerStopsOpenTargetAtSentenceBoundary()
    {
        var planner =
            new DesktopLocalActionPlanner();

        var foreground =
            new ComputerWindowInfo(
                "0x100",
                "Trình duyệt",
                "msedge",
                100,
                true,
                0,
                0,
                1280,
                720);

        var state =
            new ComputerOperatorDesktopState(
                DateTimeOffset.UtcNow,
                foreground,
                [foreground],
                0,
                0,
                1920,
                1080,
                DesktopCaptureScopes.VirtualDesktop,
                null,
                false);

        var planned =
            planner.TryPlan(
                "Mở Demo App. Vào màn hình tiếp theo rồi hoàn tất.",
                state,
                string.Empty,
                out var decision);

        Require(
            planned &&
            decision.Action == "press-hotkey" &&
            decision.CurrentSubgoal.Contains(
                "Demo App",
                StringComparison.OrdinalIgnoreCase) &&
            !decision.CurrentSubgoal.Contains(
                "Vào màn hình",
                StringComparison.OrdinalIgnoreCase),
            "Local planner chưa cắt open-app target tại dấu chấm kết thúc câu.");
    }

    private static void CheckLocalPlannerYieldsAfterLoopDirective()
    {
        var planner =
            new DesktopLocalActionPlanner();

        var foreground =
            new ComputerWindowInfo(
                "0x200",
                "Client hiện tại",
                "client",
                200,
                true,
                0,
                0,
                1280,
                720);

        var state =
            new ComputerOperatorDesktopState(
                DateTimeOffset.UtcNow,
                foreground,
                [foreground],
                0,
                0,
                1920,
                1080,
                DesktopCaptureScopes.VirtualDesktop,
                null,
                false);

        var planned =
            planner.TryPlan(
                "Mở Demo App. Vào màn hình tiếp theo rồi hoàn tất.",
                state,
                "CHỈ DẪN THOÁT VÒNG LẶP: BẮT BUỘC đổi chiến lược. Không lặp lại cùng action/target.",
                out _);

        Require(
            !planned,
            "Local planner vẫn chiếm quyền sau khi loop guard đã bắt buộc đổi chiến lược.");
    }

    private static void CheckLocalPlannerYieldsAfterFailedSearchLaunch()
    {
        var planner =
            new DesktopLocalActionPlanner();

        var search =
            new ComputerWindowInfo(
                "0x300",
                "Search",
                "SearchHost",
                300,
                true,
                0,
                0,
                1200,
                900);

        var state =
            new ComputerOperatorDesktopState(
                DateTimeOffset.UtcNow,
                search,
                [search],
                0,
                0,
                1920,
                1080,
                DesktopCaptureScopes.VirtualDesktop,
                null,
                false);

        var history =
            """
LOCAL-SHELL-TYPED:Demo App
STEP 2: VERIFY-FAILED press-key|key=enter|effect=windows search khởi chạy ứng dụng phù hợp với từ khóa demo app — không tạo transition.
""";

        var planned =
            planner.TryPlan(
                "Mở Demo App. Vào màn hình tiếp theo.",
                state,
                history,
                out _);

        Require(
            !planned,
            "Local planner vẫn lặp Enter sau khi Search launch đã verification-failed.");
    }

    private static void CheckLocalPlannerWaitsAfterVerifiedSearchLaunch()
    {
        var planner =
            new DesktopLocalActionPlanner();

        var state =
            new ComputerOperatorDesktopState(
                DateTimeOffset.UtcNow,
                ForegroundWindow: null,
                Windows: Array.Empty<ComputerWindowInfo>(),
                FrameLeft: 0,
                FrameTop: 0,
                FrameWidth: 1920,
                FrameHeight: 1080,
                CaptureScope: DesktopCaptureScopes.VirtualDesktop,
                CaptureWindowId: null,
                CaptureWindowWasForeground: false);

        var verifiedHistory =
            """
LOCAL-SHELL-TYPED:Demo App
STEP 2: VERIFIED press-key|key=enter|effect=windows search khởi chạy ứng dụng phù hợp với từ khóa demo app — transition rõ; EXPECTED: Windows Search khởi chạy ứng dụng phù hợp với từ khóa Demo App.
""";

        var firstPlanned =
            planner.TryPlan(
                "Mở Demo App. Vào màn hình tiếp theo.",
                state,
                verifiedHistory,
                out var firstDecision);

        var exhaustedHistory =
            verifiedHistory +
            """
STEP 3: WAIT — LOCAL-LAUNCH-GRACE: chờ 1
STEP 4: WAIT — LOCAL-LAUNCH-GRACE: chờ 2
STEP 5: WAIT — LOCAL-LAUNCH-GRACE: chờ 3
""";

        var exhaustedPlanned =
            planner.TryPlan(
                "Mở Demo App. Vào màn hình tiếp theo.",
                state,
                exhaustedHistory,
                out _);

        Require(
            firstPlanned &&
            firstDecision.Action == "wait" &&
            firstDecision.Reason.Contains(
                "LOCAL-LAUNCH-GRACE",
                StringComparison.OrdinalIgnoreCase) &&
            !exhaustedPlanned,
            "Local planner chưa chờ launch có giới hạn hoặc vẫn quay lại Search sau khi grace đã cạn.");
    }

    private static void CheckLocalPlannerYieldsOnTransitionalForegroundAfterLaunch()
    {
        var planner =
            new DesktopLocalActionPlanner();

        var launcher =
            new ComputerWindowInfo(
                "0x450",
                "Generic Launcher",
                "launcher-shell",
                450,
                true,
                0,
                0,
                1280,
                720);

        var state =
            new ComputerOperatorDesktopState(
                DateTimeOffset.UtcNow,
                launcher,
                [launcher],
                0,
                0,
                1920,
                1080,
                DesktopCaptureScopes.VirtualDesktop,
                null,
                false);

        var history =
            """
STEP 1: VERIFIED press-hotkey|keys=WIN+S|effect=windows search xuất hiện và nhận focus — transition rõ.
LOCAL-SHELL-TYPED:Demo App
STEP 2: VERIFIED press-key|key=ENTER|effect=windows search khởi chạy ứng dụng phù hợp với từ khóa demo app — transition rõ; EXPECTED: Windows Search khởi chạy ứng dụng phù hợp với từ khóa Demo App.
""";

        var planned =
            planner.TryPlan(
                "Mở Demo App. Sau khi ứng dụng khởi động hoàn toàn, tiếp tục vào màn hình tiếp theo.",
                state,
                history,
                out _);

        Require(
            !planned,
            "Sau launch đã xác minh và foreground mới xuất hiện, Local Planner không được mở lại Windows Search chỉ vì tên cửa sổ trung gian không khớp literal target.");
    }

    private static void CheckLocalPlannerScopesTypedMarkerToCurrentSearchSession()
    {
        var planner =
            new DesktopLocalActionPlanner();

        var search =
            new ComputerWindowInfo(
                "0x451",
                "Search",
                "SearchHost",
                451,
                true,
                0,
                0,
                1200,
                900);

        var state =
            new ComputerOperatorDesktopState(
                DateTimeOffset.UtcNow,
                search,
                [search],
                0,
                0,
                1920,
                1080,
                DesktopCaptureScopes.VirtualDesktop,
                null,
                false);

        var history =
            """
STEP 1: VERIFIED press-hotkey|keys=WIN+S|effect=windows search xuất hiện và nhận focus — transition rõ.
LOCAL-SHELL-TYPED:Demo App
STEP 2: VERIFIED press-key|key=ENTER|effect=windows search khởi chạy ứng dụng phù hợp với từ khóa demo app — transition rõ.
STEP 3: VERIFIED press-hotkey|keys=WIN+S|effect=windows search xuất hiện và nhận focus — transition rõ.
""";

        var planned =
            planner.TryPlan(
                "Mở Demo App. Vào màn hình tiếp theo.",
                state,
                history,
                out var decision);

        Require(
            planned &&
            decision.Action == "type-text" &&
            string.Equals(
                decision.Text,
                "Demo App",
                StringComparison.OrdinalIgnoreCase),
            "Marker LOCAL-SHELL-TYPED từ Search session cũ không được phép làm planner bấm Enter trong Search session mới.");
    }

    private static void CheckComputerOperatorVisionFallbackIncludesBadRequest()
    {
        Require(
            ComputerOperatorVisionRouter.IsFallbackEligibleForAcceptance(
                System.Net.HttpStatusCode.BadRequest) &&
            ComputerOperatorVisionRouter.IsFallbackEligibleForAcceptance(
                System.Net.HttpStatusCode.ServiceUnavailable) &&
            !ComputerOperatorVisionRouter.IsFallbackEligibleForAcceptance(
                System.Net.HttpStatusCode.Unauthorized),
            "HTTP 400/503 của một vision provider phải cho phép thử provider khác, còn lỗi auth 401 không được coi là fallback transient.");
    }


    private static void CheckStructuredVerifierToggleState()
    {
        var verifier =
            new StructuredDesktopVerificationService();

        var node =
            new UnifiedStructuredSceneNode(
                Id: "root:wifi",
                ParentId: "root",
                Depth: 1,
                Role: "CheckBox",
                Name: "WiFi",
                AutomationId: "wifi",
                ClassName: "CheckBox",
                IsEnabled: true,
                IsFocused: false,
                IsVisible: true,
                DesktopLeft: 100,
                DesktopTop: 100,
                Width: 120,
                Height: 30,
                FrameLeft: 100,
                FrameTop: 100,
                Capabilities: ["Toggle"],
                Children: Array.Empty<string>(),
                ToggleState: "On");

        var graph =
            new UnifiedStructuredSceneGraph(
                WindowId: "0x1234",
                WindowTitle: "Acceptance",
                ProcessName: "acceptance",
                RootId: "root",
                CapturedAtUtc: DateTimeOffset.UtcNow,
                Nodes: [node],
                Source: "acceptance",
                Detail: "acceptance");

        var decision = BuildStructuredDecision(
            "structured-toggle",
            "root:wifi",
            "structured-state:toggle=On; WiFi phải bật.");

        var result = verifier.Verify(
            decision,
            graph);

        Require(
            result.Status == StructuredVerificationStatus.Verified &&
            result.Confidence >= 0.99,
            "Structured verifier chưa xác minh Toggle state deterministic.");
    }

    private static void CheckStructuredVerifierValueReadback()
    {
        var verifier =
            new StructuredDesktopVerificationService();

        var node =
            new UnifiedStructuredSceneNode(
                Id: "root:name",
                ParentId: "root",
                Depth: 1,
                Role: "Edit",
                Name: "Name",
                AutomationId: "name",
                ClassName: "Edit",
                IsEnabled: true,
                IsFocused: true,
                IsVisible: true,
                DesktopLeft: 100,
                DesktopTop: 100,
                Width: 240,
                Height: 36,
                FrameLeft: 100,
                FrameTop: 100,
                Capabilities: ["Value"],
                Children: Array.Empty<string>(),
                Value: "Alice",
                IsSensitive: false);

        var graph =
            new UnifiedStructuredSceneGraph(
                WindowId: "0x1234",
                WindowTitle: "Acceptance",
                ProcessName: "acceptance",
                RootId: "root",
                CapturedAtUtc: DateTimeOffset.UtcNow,
                Nodes: [node],
                Source: "acceptance",
                Detail: "acceptance");

        var decision = BuildStructuredDecision(
            "structured-set-value",
            "root:name",
            "Name phải có giá trị Alice",
            text: "Alice");

        var result = verifier.Verify(
            decision,
            graph);

        Require(
            result.Status == StructuredVerificationStatus.Verified &&
            result.Confidence >= 0.99,
            "Structured verifier chưa xác minh ValuePattern bằng readback.");
    }

    private static void CheckStructuredTargetRevalidationRejectsStaleScene()
    {
        var revalidator =
            new StructuredTargetRevalidator();

        var decision =
            BuildStructuredDecision(
                "structured-invoke",
                "root:save",
                "Save phản hồi.") with
            {
                TargetLabel = "Save"
            };

        var graph =
            new UnifiedStructuredSceneGraph(
                WindowId: "0x1234",
                WindowTitle: "Acceptance",
                ProcessName: "acceptance",
                RootId: "root",
                CapturedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-10),
                Nodes:
                [
                    new UnifiedStructuredSceneNode(
                        Id: "root:save",
                        ParentId: "root",
                        Depth: 1,
                        Role: "Button",
                        Name: "Save",
                        AutomationId: "save",
                        ClassName: "Button",
                        IsEnabled: true,
                        IsFocused: false,
                        IsVisible: true,
                        DesktopLeft: 100,
                        DesktopTop: 100,
                        Width: 120,
                        Height: 36,
                        FrameLeft: 100,
                        FrameTop: 100,
                        Capabilities: ["Invoke"],
                        Children: Array.Empty<string>())
                ],
                Source: "acceptance",
                Detail: "acceptance");

        var result =
            revalidator.Revalidate(
                decision,
                graph,
                DateTimeOffset.UtcNow);

        Require(
            result.Status ==
                StructuredTargetRevalidationStatus.Rejected &&
            !result.SafeToExecute,
            "Structured target revalidation vẫn cho execute scene stale.");
    }

    private static void CheckStructuredTargetRevalidationRemapsUniqueSemanticTarget()
    {
        var revalidator =
            new StructuredTargetRevalidator();

        var decision =
            BuildStructuredDecision(
                "structured-invoke",
                "root:old-save",
                "Save phản hồi.") with
            {
                TargetLabel = "Save"
            };

        var graph =
            new UnifiedStructuredSceneGraph(
                WindowId: "0x1234",
                WindowTitle: "Acceptance",
                ProcessName: "acceptance",
                RootId: "root",
                CapturedAtUtc: DateTimeOffset.UtcNow,
                Nodes:
                [
                    new UnifiedStructuredSceneNode(
                        Id: "root:new-save",
                        ParentId: "root",
                        Depth: 1,
                        Role: "Button",
                        Name: "Save",
                        AutomationId: "saveButton",
                        ClassName: "Button",
                        IsEnabled: true,
                        IsFocused: false,
                        IsVisible: true,
                        DesktopLeft: 140,
                        DesktopTop: 120,
                        Width: 120,
                        Height: 36,
                        FrameLeft: 140,
                        FrameTop: 120,
                        Capabilities: ["Invoke"],
                        Children: Array.Empty<string>())
                ],
                Source: "acceptance",
                Detail: "acceptance");

        var result =
            revalidator.Revalidate(
                decision,
                graph,
                DateTimeOffset.UtcNow);

        Require(
            result.Status ==
                StructuredTargetRevalidationStatus.Remapped &&
            result.SafeToExecute &&
            result.Decision.TargetElementId ==
                "root:new-save" &&
            result.Decision.BoxLeft == 140 &&
            result.Decision.BoxTop == 120,
            "Structured target revalidation chưa remap token mới theo semantic target duy nhất.");
    }

    private static void CheckStructuredCapabilityConfidenceDistinguishesLegacyFallback()
    {
        var revalidator =
            new StructuredTargetRevalidator();

        UnifiedStructuredSceneGraph Graph(
            string capability) =>
            new(
                WindowId: "0x1234",
                WindowTitle: "Acceptance",
                ProcessName: "acceptance",
                RootId: "root",
                CapturedAtUtc: DateTimeOffset.UtcNow,
                Nodes:
                [
                    new UnifiedStructuredSceneNode(
                        Id: "root:target",
                        ParentId: "root",
                        Depth: 1,
                        Role: "Button",
                        Name: "Target",
                        AutomationId: "target",
                        ClassName: "Button",
                        IsEnabled: true,
                        IsFocused: false,
                        IsVisible: true,
                        DesktopLeft: 100,
                        DesktopTop: 100,
                        Width: 120,
                        Height: 36,
                        FrameLeft: 100,
                        FrameTop: 100,
                        Capabilities: [capability],
                        Children: Array.Empty<string>())
                ],
                Source: "acceptance",
                Detail: "acceptance");

        var native =
            revalidator.Revalidate(
                BuildStructuredDecision(
                    "structured-invoke",
                    "root:target",
                    "invoke"),
                Graph("Invoke"),
                DateTimeOffset.UtcNow);

        var legacy =
            revalidator.Revalidate(
                BuildStructuredDecision(
                    "structured-legacy-default",
                    "root:target",
                    "legacy"),
                Graph("LegacyIAccessible"),
                DateTimeOffset.UtcNow);

        Require(
            native.SafeToExecute &&
            legacy.SafeToExecute &&
            native.Confidence > legacy.Confidence &&
            native.Confidence >= 0.95 &&
            legacy.Confidence <= 0.80,
            "Structured capability confidence chưa phân biệt UIA native và LegacyIAccessible fallback.");
    }

    private static void CheckStructuredInvokeEventRelevance()
    {
        var desktopEvent =
            new DesktopSystemEvent(
                DesktopSystemEventKinds.StructureChanged,
                "0x1234",
                DateTimeOffset.UtcNow,
                0,
                "UIA structure changed.",
                Source: "uia3",
                SourceConfidence: 0.90,
                Corroborated: true);

        var score =
            ComputerOperatorEventRelevance.Score(
                "structured-invoke",
                "Mở dialog cài đặt.",
                desktopEvent);

        Require(
            score >= 0.90,
            $"Structured invoke chưa ưu tiên event transition đủ mạnh: {score:0.000}.");
    }

    private static void CheckStructuredInvokeLocalVerificationRequiresTransition()
    {
        var router =
            new DesktopVerificationRouter();

        var decision =
            BuildStructuredDecision(
                "structured-invoke",
                "root:next",
                "Trang tiếp theo xuất hiện.");

        var changed =
            new DesktopFastObservation(
                ScreenChanged: true,
                ChangeRatio: 0.05,
                ForegroundWindowChanged: true,
                WindowBoundsChanged: false,
                CursorMoved: false,
                MonitorChanged: false,
                DpiChanged: false,
                TargetMoved: false,
                TargetMissing: false,
                TargetLikelyOccluded: false,
                Summary: "foreground changed");

        var unchanged =
            new DesktopFastObservation(
                ScreenChanged: false,
                ChangeRatio: 0.0,
                ForegroundWindowChanged: false,
                WindowBoundsChanged: false,
                CursorMoved: false,
                MonitorChanged: false,
                DpiChanged: false,
                TargetMoved: false,
                TargetMissing: false,
                TargetLikelyOccluded: false,
                Summary: "unchanged");

        var changedRoute =
            router.Route(
                decision,
                changed,
                frameDifference: null);

        var unchangedRoute =
            router.Route(
                decision,
                unchanged,
                new DesktopFrameDifference(
                    true,
                    0.0,
                    0,
                    100,
                    0,
                    0,
                    0,
                    0,
                    0,
                    "acceptance"));

        Require(
            changedRoute.Route ==
                DesktopVerificationRoute.LocalVerified &&
            unchangedRoute.Route ==
                DesktopVerificationRoute.GeminiRequired,
            "Structured invoke verifier đang pass khi chưa có transition local rõ ràng.");
    }

    private static void CheckStructuredVerifierInvokeIsInconclusive()
    {
        var verifier =
            new StructuredDesktopVerificationService();

        var decision = BuildStructuredDecision(
            "structured-invoke",
            "root:next",
            "Trang tiếp theo xuất hiện.");

        var result = verifier.Verify(
            decision,
            graph: null);

        Require(
            result.Status == StructuredVerificationStatus.Inconclusive,
            "Structured invoke không được coi là failure chỉ vì scene graph sau transition chưa có target.");
    }

    private static DesktopOperatorDecision BuildStructuredDecision(
        string action,
        string targetElementId,
        string expectedEffect,
        string text = "") =>
        new(
            State: "acceptance",
            Plan: "acceptance",
            CurrentSubgoal: "acceptance",
            GoalProgress: 0,
            VerifiedMilestones: Array.Empty<string>(),
            Action: action,
            Query: string.Empty,
            Text: text,
            Key: string.Empty,
            Keys: Array.Empty<string>(),
            Url: string.Empty,
            TargetLabel: "acceptance",
            CoordinateSpace: ComputerCoordinateSpaces.ImagePixel,
            CoordinateWindowId: "0x1234",
            ImageX: 0,
            ImageY: 0,
            EndImageX: 0,
            EndImageY: 0,
            NormalizedX: 0,
            NormalizedY: 0,
            EndNormalizedX: 0,
            EndNormalizedY: 0,
            BoxLeft: 0,
            BoxTop: 0,
            BoxWidth: 0,
            BoxHeight: 0,
            BoxNormalizedLeft: 0,
            BoxNormalizedTop: 0,
            BoxNormalizedWidth: 0,
            BoxNormalizedHeight: 0,
            ScrollDelta: 0,
            ExpectedEffect: expectedEffect,
            Confidence: 0.99,
            Reason: "acceptance",
            SceneElements: Array.Empty<DesktopSceneElement>(),
            TargetElementId: targetElementId);

    private static void CheckStructuredInvokeFallsBackToLegacyAccessible()
    {
        var planner = new DesktopLocalActionPlanner();
        var foreground = new ComputerWindowInfo(
            "0x1234",
            "Legacy App",
            "legacy",
            10,
            true,
            0,
            0,
            1000,
            700);

        var node = new StructuredDesktopNode(
            Token: "root:legacy",
            ParentToken: "root",
            Depth: 2,
            Role: "Button",
            Name: "Legacy Action",
            AutomationId: string.Empty,
            ClassName: "LegacyButton",
            IsEnabled: true,
            IsFocused: false,
            IsOffscreen: false,
            Left: 220,
            Top: 180,
            Width: 160,
            Height: 40,
            Patterns: ["LegacyIAccessible"]);

        var structured = new StructuredDesktopSnapshot(
            WindowId: foreground.WindowId,
            RootToken: "root",
            CapturedAtUtc: DateTimeOffset.UtcNow,
            NodeCount: 1,
            MaximumNodes: 240,
            MaximumDepth: 7,
            Nodes: [node],
            Source: "flaui-uia3",
            Detail: "acceptance");

        var state = new ComputerOperatorDesktopState(
            DateTimeOffset.UtcNow,
            foreground,
            [foreground],
            FrameLeft: 0,
            FrameTop: 0,
            FrameWidth: 1000,
            FrameHeight: 700,
            CaptureScope: DesktopCaptureScopes.Window,
            CaptureWindowId: foreground.WindowId,
            CaptureWindowWasForeground: true,
            StructuredScene: structured);

        var planned = planner.TryPlan(
            "Bấm Legacy Action",
            state,
            string.Empty,
            out var decision);

        Require(
            planned &&
            decision.Action == "structured-legacy-default" &&
            decision.TargetElementId == "root:legacy",
            "Structured planner chưa fallback Invoke intent sang LegacyIAccessible đúng cách.");
    }

    private static void CheckStructuredToggleUsesCurrentState()
    {
        var planner = new DesktopLocalActionPlanner();
        var foreground = new ComputerWindowInfo(
            "0x1234",
            "Acceptance App",
            "acceptance",
            10,
            true,
            0,
            0,
            1000,
            700);

        ComputerOperatorDesktopState BuildState(
            string toggleState)
        {
            var node = new StructuredDesktopNode(
                Token: "root:wifi",
                ParentToken: "root",
                Depth: 2,
                Role: "CheckBox",
                Name: "WiFi",
                AutomationId: "wifiToggle",
                ClassName: "CheckBox",
                IsEnabled: true,
                IsFocused: false,
                IsOffscreen: false,
                Left: 200,
                Top: 160,
                Width: 180,
                Height: 36,
                Patterns: ["Toggle"],
                ToggleState: toggleState);

            var structured = new StructuredDesktopSnapshot(
                WindowId: foreground.WindowId,
                RootToken: "root",
                CapturedAtUtc: DateTimeOffset.UtcNow,
                NodeCount: 1,
                MaximumNodes: 240,
                MaximumDepth: 7,
                Nodes: [node],
                Source: "flaui-uia3",
                Detail: "acceptance");

            return new ComputerOperatorDesktopState(
                DateTimeOffset.UtcNow,
                foreground,
                [foreground],
                FrameLeft: 0,
                FrameTop: 0,
                FrameWidth: 1000,
                FrameHeight: 700,
                CaptureScope: DesktopCaptureScopes.Window,
                CaptureWindowId: foreground.WindowId,
                CaptureWindowWasForeground: true,
                StructuredScene: structured);
        }

        var offPlanned = planner.TryPlan(
            "Bật WiFi",
            BuildState("Off"),
            string.Empty,
            out var offDecision);

        var onPlanned = planner.TryPlan(
            "Bật WiFi",
            BuildState("On"),
            string.Empty,
            out var onDecision);

        Require(
            offPlanned &&
            onPlanned &&
            offDecision.Action == "structured-toggle" &&
            onDecision.Action == "complete",
            "Structured toggle chưa dùng current state để tránh đảo ngược trạng thái đã đúng.");
    }

    private static void CheckStructuredPlannerUsesIntentSpecificPattern()
    {
        var planner = new DesktopLocalActionPlanner();
        var foreground = new ComputerWindowInfo(
            "0x1234",
            "Acceptance App",
            "acceptance",
            10,
            true,
            0,
            0,
            1000,
            700);

        var node = new StructuredDesktopNode(
            Token: "root:option",
            ParentToken: "root",
            Depth: 2,
            Role: "ListItem",
            Name: "Option A",
            AutomationId: "optionA",
            ClassName: "ListItem",
            IsEnabled: true,
            IsFocused: false,
            IsOffscreen: false,
            Left: 200,
            Top: 160,
            Width: 180,
            Height: 36,
            Patterns: ["Invoke", "SelectionItem"]);

        var structured = new StructuredDesktopSnapshot(
            WindowId: foreground.WindowId,
            RootToken: "root",
            CapturedAtUtc: DateTimeOffset.UtcNow,
            NodeCount: 1,
            MaximumNodes: 240,
            MaximumDepth: 7,
            Nodes: [node],
            Source: "flaui-uia3",
            Detail: "acceptance");

        var state = new ComputerOperatorDesktopState(
            DateTimeOffset.UtcNow,
            foreground,
            [foreground],
            FrameLeft: 0,
            FrameTop: 0,
            FrameWidth: 1000,
            FrameHeight: 700,
            CaptureScope: DesktopCaptureScopes.Window,
            CaptureWindowId: foreground.WindowId,
            CaptureWindowWasForeground: true,
            StructuredScene: structured);

        var selectPlanned = planner.TryPlan(
            "Chọn Option A",
            state,
            string.Empty,
            out var selectDecision);

        var invokePlanned = planner.TryPlan(
            "Bấm Option A",
            state,
            string.Empty,
            out var invokeDecision);

        Require(
            selectPlanned &&
            invokePlanned &&
            selectDecision.Action == "structured-select" &&
            invokeDecision.Action == "structured-invoke",
            "Structured planner chưa phân biệt đúng SelectionItem và Invoke theo intent.");
    }

    private static void CheckStructuredFirstPlannerUsesValuePattern()
    {
        var planner = new DesktopLocalActionPlanner();
        var foreground = new ComputerWindowInfo(
            "0x1234",
            "Acceptance App",
            "acceptance",
            10,
            true,
            100,
            80,
            900,
            700);

        var node = new StructuredDesktopNode(
            Token: "root:name",
            ParentToken: "root",
            Depth: 2,
            Role: "Edit",
            Name: "Name",
            AutomationId: "nameField",
            ClassName: "Edit",
            IsEnabled: true,
            IsFocused: false,
            IsOffscreen: false,
            Left: 320,
            Top: 220,
            Width: 240,
            Height: 40,
            Patterns: ["Value"]);

        var structured = new StructuredDesktopSnapshot(
            WindowId: foreground.WindowId,
            RootToken: "root",
            CapturedAtUtc: DateTimeOffset.UtcNow,
            NodeCount: 1,
            MaximumNodes: 240,
            MaximumDepth: 7,
            Nodes: [node],
            Source: "flaui-uia3",
            Detail: "acceptance");

        var state = new ComputerOperatorDesktopState(
            DateTimeOffset.UtcNow,
            foreground,
            [foreground],
            FrameLeft: 100,
            FrameTop: 80,
            FrameWidth: 900,
            FrameHeight: 700,
            CaptureScope: DesktopCaptureScopes.Window,
            CaptureWindowId: foreground.WindowId,
            CaptureWindowWasForeground: true,
            StructuredScene: structured);

        var planned = planner.TryPlan(
            "Nhập Alice vào ô Name",
            state,
            string.Empty,
            out var decision);

        Require(
            planned &&
            decision.Action == "structured-set-value" &&
            decision.Text == "Alice" &&
            decision.TargetElementId == "root:name" &&
            decision.CoordinateWindowId == foreground.WindowId,
            "Structured-first planner chưa dùng ValuePattern cho field rõ ràng.");
    }

    private static void CheckStructuredFirstPlannerUsesUiaTarget()
    {
        var planner = new DesktopLocalActionPlanner();
        var foreground = new ComputerWindowInfo(
            "0x1234",
            "Acceptance App",
            "acceptance",
            10,
            true,
            100,
            80,
            900,
            700);

        var node = new StructuredDesktopNode(
            Token: "root:save",
            ParentToken: "root",
            Depth: 2,
            Role: "Button",
            Name: "Save",
            AutomationId: "saveButton",
            ClassName: "Button",
            IsEnabled: true,
            IsFocused: false,
            IsOffscreen: false,
            Left: 420,
            Top: 260,
            Width: 120,
            Height: 44,
            Patterns: ["Invoke"]);

        var structured = new StructuredDesktopSnapshot(
            WindowId: foreground.WindowId,
            RootToken: "root",
            CapturedAtUtc: DateTimeOffset.UtcNow,
            NodeCount: 1,
            MaximumNodes: 240,
            MaximumDepth: 7,
            Nodes: [node],
            Source: "flaui-uia3",
            Detail: "acceptance");

        var state = new ComputerOperatorDesktopState(
            DateTimeOffset.UtcNow,
            foreground,
            [foreground],
            FrameLeft: 100,
            FrameTop: 80,
            FrameWidth: 900,
            FrameHeight: 700,
            CaptureScope: DesktopCaptureScopes.Window,
            CaptureWindowId: foreground.WindowId,
            CaptureWindowWasForeground: true,
            StructuredScene: structured);

        var planned = planner.TryPlan(
            "Bấm nút Save",
            state,
            string.Empty,
            out var decision);

        Require(
            planned &&
            decision.Action == "structured-invoke" &&
            decision.TargetElementId == "root:save" &&
            decision.TargetLabel == "Save" &&
            decision.CoordinateWindowId == foreground.WindowId &&
            decision.BoxLeft == 320 &&
            decision.BoxTop == 180 &&
            decision.BoxWidth == 120 &&
            decision.BoxHeight == 44 &&
            decision.Confidence >= 0.94,
            "Structured-first planner chưa chuyển UIA target thành structured Invoke action theo token.");
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

    private static void CheckActionStateMachineInconclusiveVerificationReplansSafely()
    {
        var machine = new ComputerOperatorActionStateMachine();

        machine.StartObservation("test");
        machine.MoveTo(ComputerOperatorActionState.Plan, "test");
        machine.MoveTo(ComputerOperatorActionState.Target, "test");
        machine.MoveTo(ComputerOperatorActionState.Execute, "test");
        machine.MoveTo(ComputerOperatorActionState.Verify, "test");

        var diagnose = machine.MoveTo(
            ComputerOperatorActionState.Diagnose,
            "verification inconclusive");

        var replan = machine.MoveTo(
            ComputerOperatorActionState.Replan,
            "observe again");

        Require(
            diagnose.State == ComputerOperatorActionState.Diagnose &&
            replan.State == ComputerOperatorActionState.Replan,
            "Verification inconclusive phải đi Verify -> Diagnose -> Replan.");
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

    private static void CheckBrowserRouterPrefersPlaywright()
    {
        var playwright =
            new AcceptancePlaywrightBrowserExecutionBackend(
                confidence: 0.995,
                success: true,
                verified: true);

        var http =
            new AcceptanceHttpBrowserExecutionBackend(
                confidence: 0.97,
                success: true,
                verified: true);

        var router =
            new CompositeBrowserExecutionBackend(
                playwright,
                http);

        var result = router.ExecuteAsync(
                new ExecutionAgentRequest(
                    "Mở https://example.com",
                    ExecutionAgentChannels.Browser))
            .GetAwaiter()
            .GetResult();

        Require(
            playwright.CallCount == 1 &&
            http.CallCount == 0 &&
            result.Verified &&
            result.Engine == "acceptance-playwright",
            "Browser router chưa ưu tiên Playwright khi structured DOM verify được.");
    }

    private static void CheckBrowserRouterFallsBackToHttp()
    {
        var playwright =
            new AcceptancePlaywrightBrowserExecutionBackend(
                confidence: 0.995,
                success: false,
                verified: false);

        var http =
            new AcceptanceHttpBrowserExecutionBackend(
                confidence: 0.97,
                success: true,
                verified: true);

        var router =
            new CompositeBrowserExecutionBackend(
                playwright,
                http);

        var result = router.ExecuteAsync(
                new ExecutionAgentRequest(
                    "Mở https://example.com",
                    ExecutionAgentChannels.Browser))
            .GetAwaiter()
            .GetResult();

        Require(
            playwright.CallCount == 1 &&
            http.CallCount == 1 &&
            result.Verified &&
            result.Engine == "acceptance-http",
            "Browser router chưa fallback HTTP khi Playwright chưa tạo evidence đủ.");
    }

    private static void CheckBrowserRouterUsesHigherConfidenceBackend()
    {
        var playwright =
            new AcceptancePlaywrightBrowserExecutionBackend(
                confidence: 0.70,
                success: true,
                verified: true);

        var http =
            new AcceptanceHttpBrowserExecutionBackend(
                confidence: 0.97,
                success: true,
                verified: true);

        var router =
            new CompositeBrowserExecutionBackend(
                playwright,
                http);

        var result = router.ExecuteAsync(
                new ExecutionAgentRequest(
                    "Mở https://example.com",
                    ExecutionAgentChannels.Browser))
            .GetAwaiter()
            .GetResult();

        Require(
            playwright.CallCount == 0 &&
            http.CallCount == 1 &&
            result.Engine == "acceptance-http",
            "Browser router chưa tôn trọng capability/confidence khi chọn backend.");
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

    private static void CheckUniversalRouterUsesPlaywrightCapability()
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

        var discovery =
            new AcceptanceCapabilityDiscoveryService(
                new UniversalCapabilitySnapshot(
                    PersonalAiRelease.Version,
                    DateTimeOffset.UtcNow,
                    [
                        new UniversalCapabilitySignal(
                            "browser.playwright-dom",
                            ExecutionAgentChannels.Browser,
                            "Playwright + Microsoft Edge",
                            Available: true,
                            UniversalCapabilityReliability.Structured,
                            0.05,
                            Priority: 1,
                            "acceptance")
                    ]));

        var router = new UniversalTaskRouter(
            registry,
            new ExecutionGateway(registry),
            toolCapabilities: null,
            capabilityDiscovery: discovery);

        var route = router.Preview(
            new UniversalTaskRouteRequest(
                "Mở https://example.com và đọc trang"));

        var browser = route.Candidates.Single(
            item => item.Channel ==
                ExecutionAgentChannels.Browser);

        Require(
            route.SelectedChannel ==
                ExecutionAgentChannels.Browser &&
            browser.Reason.Contains(
                "capability:browser.playwright-dom",
                StringComparison.OrdinalIgnoreCase),
            $"Router chưa dùng Playwright runtime capability: {browser.Reason}");
    }

    private static void CheckUniversalRouterUsesFlaUiCapability()
    {
        var registry = new ExecutionAgentRegistry(
            new IExecutionAgent[]
            {
                new AcceptanceExecutionAgent(
                    "execution.computer.acceptance",
                    ExecutionAgentChannels.Computer)
            });

        var discovery =
            new AcceptanceCapabilityDiscoveryService(
                new UniversalCapabilitySnapshot(
                    PersonalAiRelease.Version,
                    DateTimeOffset.UtcNow,
                    [
                        new UniversalCapabilitySignal(
                            "desktop.flaui-uia3",
                            ExecutionAgentChannels.Computer,
                            "FlaUI / UI Automation 3",
                            Available: true,
                            UniversalCapabilityReliability.Structured,
                            0.04,
                            Priority: 1,
                            "acceptance")
                    ]));

        var router = new UniversalTaskRouter(
            registry,
            new ExecutionGateway(registry),
            toolCapabilities: null,
            capabilityDiscovery: discovery);

        var route = router.Preview(
            new UniversalTaskRouteRequest(
                "Mở ứng dụng desktop bằng chuột và bàn phím"));

        var computer = route.Candidates.Single(
            item => item.Channel ==
                ExecutionAgentChannels.Computer);

        Require(
            computer.Reason.Contains(
                "capability:desktop.flaui-uia3",
                StringComparison.OrdinalIgnoreCase),
            $"Router chưa dùng FlaUI runtime capability: {computer.Reason}");
    }

    private static void CheckUniversalRouterIgnoresUnavailableCapability()
    {
        var registry = new ExecutionAgentRegistry(
            new IExecutionAgent[]
            {
                new AcceptanceExecutionAgent(
                    "execution.browser.acceptance",
                    ExecutionAgentChannels.Browser)
            });

        var discovery =
            new AcceptanceCapabilityDiscoveryService(
                new UniversalCapabilitySnapshot(
                    PersonalAiRelease.Version,
                    DateTimeOffset.UtcNow,
                    [
                        new UniversalCapabilitySignal(
                            "browser.playwright-dom",
                            ExecutionAgentChannels.Browser,
                            "Playwright + Microsoft Edge",
                            Available: false,
                            UniversalCapabilityReliability.Structured,
                            0,
                            Priority: 1,
                            "acceptance-unavailable"),
                        new UniversalCapabilitySignal(
                            "browser.http-html",
                            ExecutionAgentChannels.Browser,
                            "Browser Agent HTTP/HTML",
                            Available: true,
                            UniversalCapabilityReliability.Fallback,
                            0.02,
                            Priority: 2,
                            "acceptance-http")
                    ]));

        var router = new UniversalTaskRouter(
            registry,
            new ExecutionGateway(registry),
            toolCapabilities: null,
            capabilityDiscovery: discovery);

        var route = router.Preview(
            new UniversalTaskRouteRequest(
                "Mở https://example.com"));

        var browser = route.Candidates.Single(
            item => item.Channel ==
                ExecutionAgentChannels.Browser);

        Require(
            !browser.Reason.Contains(
                "capability:browser.playwright-dom",
                StringComparison.OrdinalIgnoreCase) &&
            browser.Reason.Contains(
                "capability:browser.http-html",
                StringComparison.OrdinalIgnoreCase),
            $"Router vẫn quảng bá capability không khả dụng: {browser.Reason}");
    }

    private static void CheckAdaptiveWaitCompletesOnVerifiedEvidence()
    {
        var engine = new AdaptiveVerificationWaitEngine();
        var calls = 0;

        var result = engine.WaitAsync(
                _ =>
                {
                    calls++;
                    return Task.FromResult(
                        calls >= 2
                            ? new AdaptiveProgressSample(
                                AdaptiveWaitStatuses.Verified,
                                0.99,
                                "final evidence",
                                MeaningfulProgress: true)
                            : new AdaptiveProgressSample(
                                AdaptiveWaitStatuses.Progressing,
                                0.95,
                                "foreground changed",
                                MeaningfulProgress: true));
                },
                new AdaptiveWaitPolicy(
                    TimeSpan.FromMilliseconds(1),
                    TimeSpan.FromMilliseconds(50),
                    TimeSpan.FromMilliseconds(200),
                    0.70))
            .GetAwaiter()
            .GetResult();

        Require(
            result.Verified &&
            !result.Failed &&
            result.Samples == 2 &&
            result.ProgressHeartbeats == 1,
            "Adaptive Wait chưa complete đúng khi final evidence xuất hiện.");
    }

    private static void CheckAdaptiveWaitIgnoresWeakVisualHeartbeat()
    {
        var engine = new AdaptiveVerificationWaitEngine();

        var result = engine.WaitAsync(
                _ => Task.FromResult(
                    new AdaptiveProgressSample(
                        AdaptiveWaitStatuses.Progressing,
                        0.60,
                        "visual animation",
                        MeaningfulProgress: false)),
                new AdaptiveWaitPolicy(
                    TimeSpan.FromMilliseconds(1),
                    TimeSpan.FromMilliseconds(15),
                    TimeSpan.FromMilliseconds(100),
                    0.70))
            .GetAwaiter()
            .GetResult();

        Require(
            result.Status == AdaptiveWaitStatuses.Stalled &&
            result.ProgressHeartbeats == 0 &&
            !result.Failed,
            "Visual animation yếu vẫn đang gia hạn stall timer.");
    }

    private static void CheckAdaptiveWaitStalledIsNotFailure()
    {
        var engine = new AdaptiveVerificationWaitEngine();

        var result = engine.WaitAsync(
                _ => Task.FromResult(
                    new AdaptiveProgressSample(
                        AdaptiveWaitStatuses.Pending,
                        0.20,
                        "waiting",
                        MeaningfulProgress: false)),
                new AdaptiveWaitPolicy(
                    TimeSpan.FromMilliseconds(1),
                    TimeSpan.FromMilliseconds(12),
                    TimeSpan.FromMilliseconds(100),
                    0.70))
            .GetAwaiter()
            .GetResult();

        Require(
            result.Status == AdaptiveWaitStatuses.Stalled &&
            !result.Verified &&
            !result.Failed,
            "Stalled đang bị đánh đồng với Failed.");
    }

    private static void CheckEvidenceFusionAcceptsDeterministicEvidence()
    {
        var fusion = new UniversalEvidenceFusionEngine();

        var result = fusion.Fuse(
            [
                new UniversalEvidenceSignal(
                    "uia-value-pattern",
                    "focused-control-value",
                    Passed: true,
                    Confidence: 0.99,
                    UniversalEvidenceReliability.Deterministic,
                    "actual text == expected text")
            ]);

        Require(
            result.Status == UniversalEvidenceFusionStatuses.Verified &&
            result.GoalAchieved &&
            result.IndependentlyVerified,
            "Deterministic evidence mạnh chưa được fusion chấp nhận.");
    }

    private static void CheckEvidenceFusionRejectsStrongConflict()
    {
        var fusion = new UniversalEvidenceFusionEngine();

        var result = fusion.Fuse(
            [
                new UniversalEvidenceSignal(
                    "uia-structured",
                    "uia",
                    Passed: true,
                    Confidence: 0.95,
                    UniversalEvidenceReliability.Structured,
                    "UIA báo đúng"),
                new UniversalEvidenceSignal(
                    "process-state",
                    "process",
                    Passed: false,
                    Confidence: 0.94,
                    UniversalEvidenceReliability.StrongLocal,
                    "Process state chưa đạt")
            ]);

        Require(
            result.Status == UniversalEvidenceFusionStatuses.NeedsVerification &&
            !result.GoalAchieved,
            "Evidence Fusion vẫn tự kết luận khi nguồn mạnh xung đột.");
    }

    private static void CheckEvidenceFusionDoesNotDoubleCountCorrelatedVisualEvidence()
    {
        var fusion = new UniversalEvidenceFusionEngine();

        var result = fusion.Fuse(
            [
                new UniversalEvidenceSignal(
                    "frame-diff",
                    "visual",
                    Passed: true,
                    Confidence: 0.92,
                    UniversalEvidenceReliability.StrongLocal,
                    "frame changed"),
                new UniversalEvidenceSignal(
                    "roi-vision-local",
                    "visual",
                    Passed: true,
                    Confidence: 0.93,
                    UniversalEvidenceReliability.StrongLocal,
                    "same visual source group")
            ]);

        Require(
            result.Status == UniversalEvidenceFusionStatuses.NeedsVerification &&
            result.AcceptedSignals.Count == 1,
            "Evidence Fusion đang double-count tín hiệu visual tương quan.");
    }

    private static void CheckEventDrivenWakeAcceleratesObservation()
    {
        var wake =
            new AcceptanceObservationWakeSource(
                emitEvent: true);

        var engine =
            new AdaptiveVerificationWaitEngine(
                wake);

        var calls = 0;
        var result = engine.WaitAsync(
                _ =>
                {
                    calls++;
                    return Task.FromResult(
                        calls >= 2
                            ? new AdaptiveProgressSample(
                                AdaptiveWaitStatuses.Verified,
                                0.99,
                                "verified",
                                MeaningfulProgress: true)
                            : new AdaptiveProgressSample(
                                AdaptiveWaitStatuses.Pending,
                                0.20,
                                "pending",
                                MeaningfulProgress: false));
                },
                new AdaptiveWaitPolicy(
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromSeconds(5),
                    0.70))
            .GetAwaiter()
            .GetResult();

        Require(
            result.Verified &&
            result.EventWakeups >= 1 &&
            wake.WaitCount >= 1,
            "Event-driven wake chưa đánh thức verifier trước polling fallback.");
    }

    private static void CheckEventWakeDoesNotCreateSuccess()
    {
        var wake =
            new AcceptanceObservationWakeSource(
                emitEvent: true);

        var engine =
            new AdaptiveVerificationWaitEngine(
                wake);

        var result = engine.WaitAsync(
                _ => Task.FromResult(
                    new AdaptiveProgressSample(
                        AdaptiveWaitStatuses.Pending,
                        0.20,
                        "event chỉ đánh thức, chưa có evidence",
                        MeaningfulProgress: false)),
                new AdaptiveWaitPolicy(
                    TimeSpan.FromMilliseconds(1),
                    TimeSpan.FromMilliseconds(15),
                    TimeSpan.FromMilliseconds(100),
                    0.70))
            .GetAwaiter()
            .GetResult();

        Require(
            !result.Verified &&
            !result.Failed &&
            result.Status == AdaptiveWaitStatuses.Stalled,
            "Windows event đang bị coi nhầm là verification success.");
    }

    private static void CheckEventWakeFallsBackToPolling()
    {
        var wake =
            new AcceptanceObservationWakeSource(
                emitEvent: false);

        var engine =
            new AdaptiveVerificationWaitEngine(
                wake);

        var calls = 0;
        var result = engine.WaitAsync(
                _ =>
                {
                    calls++;
                    return Task.FromResult(
                        calls >= 2
                            ? new AdaptiveProgressSample(
                                AdaptiveWaitStatuses.Verified,
                                0.99,
                                "verified after fallback poll",
                                MeaningfulProgress: true)
                            : new AdaptiveProgressSample(
                                AdaptiveWaitStatuses.Pending,
                                0.20,
                                "pending",
                                MeaningfulProgress: false));
                },
                new AdaptiveWaitPolicy(
                    TimeSpan.FromMilliseconds(1),
                    TimeSpan.FromMilliseconds(50),
                    TimeSpan.FromMilliseconds(100),
                    0.70))
            .GetAwaiter()
            .GetResult();

        Require(
            result.Verified &&
            result.EventWakeups == 0 &&
            wake.WaitCount >= 1,
            "Không có event nhưng polling fallback chưa hoạt động.");
    }

    private static void CheckEventObservationSnapshotContract()
    {
        var source =
            new AcceptanceObservationWakeSource(
                emitEvent: true);

        var snapshot =
            source.GetSnapshot();

        Require(
            snapshot.EventDrivenAvailable &&
            snapshot.ReceivedEvents >= 0 &&
            snapshot.CoalescedEvents >= 0 &&
            snapshot.DroppedEvents >= 0 &&
            snapshot.DeliveredWakeups >= 0 &&
            snapshot.PollFallbacks >= 0 &&
            snapshot.QueuedEvents >= 0,
            "Event observation snapshot trả counters không hợp lệ.");
    }

    private static void CheckAdaptiveWaitUsesTargetAwareWake()
    {
        var wake =
            new AcceptanceObservationWakeSource(
                emitEvent: true);

        var engine =
            new AdaptiveVerificationWaitEngine(
                wake);

        var result =
            engine.WaitAsync(
                    _ => Task.FromResult(
                        new AdaptiveProgressSample(
                            AdaptiveWaitStatuses.Pending,
                            0.3,
                            "acceptance pending")),
                    new AdaptiveWaitPolicy(
                        TimeSpan.FromMilliseconds(1),
                        TimeSpan.FromMilliseconds(20),
                        TimeSpan.FromMilliseconds(40),
                        0.7),
                    "0xCAFE",
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

        Require(
            wake.LastTargetWindowId == "0xCAFE" &&
            wake.WaitCount > 0 &&
            result.EventWakeups > 0,
            "Adaptive wait chưa truyền đúng target window vào event wake source.");
    }

    private static void CheckEventRelevancePrioritizesExpectedSignals()
    {
        var valueEvent =
            new DesktopSystemEvent(
                DesktopSystemEventKinds.ValueChanged,
                "0x1",
                DateTimeOffset.UtcNow,
                0,
                "acceptance value changed",
                SourceConfidence: 1.0);

        var moveEvent =
            new DesktopSystemEvent(
                DesktopSystemEventKinds.WindowMovedOrResized,
                "0x1",
                DateTimeOffset.UtcNow,
                0,
                "acceptance moved",
                SourceConfidence: 1.0);

        var typeValue =
            ComputerOperatorEventRelevance.Score(
                "type-text",
                valueEvent);

        var typeMove =
            ComputerOperatorEventRelevance.Score(
                "type-text",
                moveEvent);

        var maximizeMove =
            ComputerOperatorEventRelevance.Score(
                "maximize",
                moveEvent);

        Require(
            typeValue >= 0.9 &&
            typeMove < 0.55 &&
            maximizeMove >= 0.9,
            "Event relevance chưa phân biệt đúng tín hiệu theo action.");
    }

    private static void CheckEventRelevanceUsesExpectedEffect()
    {
        var structureEvent =
            new DesktopSystemEvent(
                DesktopSystemEventKinds.StructureChanged,
                "0x1",
                DateTimeOffset.UtcNow,
                0,
                "acceptance structure changed",
                SourceConfidence: 1.0);

        var withoutEffect =
            ComputerOperatorEventRelevance.Score(
                "press-hotkey",
                structureEvent);

        var withEffect =
            ComputerOperatorEventRelevance.Score(
                "press-hotkey",
                "Windows Search xuất hiện.",
                structureEvent);

        Require(
            withEffect > withoutEffect &&
            withEffect >= 0.9,
            "Expected effect chưa tăng độ ưu tiên cho event phù hợp.");
    }

    private static void CheckEventSourceFusionConfidence()
    {
        var singleSource =
            new DesktopSystemEvent(
                DesktopSystemEventKinds.ValueChanged,
                "0x1",
                DateTimeOffset.UtcNow,
                0,
                "single source",
                Source: "uia3",
                SourceConfidence: 0.75,
                Corroborated: false);

        var corroborated =
            singleSource with
            {
                SourceConfidence = 0.90,
                Corroborated = true
            };

        var singleScore =
            ComputerOperatorEventRelevance.Score(
                "type-text",
                "text được nhập",
                singleSource);

        var fusedScore =
            ComputerOperatorEventRelevance.Score(
                "type-text",
                "text được nhập",
                corroborated);

        Require(
            fusedScore > singleScore &&
            fusedScore >= 0.90,
            "Event source fusion chưa tăng độ tin cậy khi có hai nguồn độc lập xác nhận.");
    }

    private static void CheckEventBurstGrouping()
    {
        var started =
            DateTimeOffset.UtcNow;

        var first =
            new DesktopSystemEvent(
                DesktopSystemEventKinds.FocusChanged,
                "0xBEEF",
                started,
                0,
                "first");

        var second =
            new DesktopSystemEvent(
                DesktopSystemEventKinds.ValueChanged,
                "0xBEEF",
                started.AddMilliseconds(80),
                0,
                "second");

        var later =
            new DesktopSystemEvent(
                DesktopSystemEventKinds.ValueChanged,
                "0xBEEF",
                started.AddMilliseconds(250),
                0,
                "later");

        Require(
            DesktopEventBurstPolicy.BelongsToSameBurst(
                first,
                second,
                TimeSpan.FromMilliseconds(120)) &&
            !DesktopEventBurstPolicy.BelongsToSameBurst(
                second,
                later,
                TimeSpan.FromMilliseconds(120)),
            "Event burst policy chưa gom đúng các event dồn dập theo cửa sổ và thời gian.");
    }

    private static void CheckStructuredResolverChoosesUniqueTarget()
    {
        var resolver = new StructuredDesktopResolver();

        var graph = new UnifiedStructuredSceneGraph(
            WindowId: "0x1234",
            WindowTitle: "Acceptance",
            ProcessName: "acceptance",
            RootId: "root",
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Nodes:
            [
                new UnifiedStructuredSceneNode(
                    Id: "root:save",
                    ParentId: "root",
                    Depth: 1,
                    Role: "Button",
                    Name: "Save",
                    AutomationId: "saveButton",
                    ClassName: "Button",
                    IsEnabled: true,
                    IsFocused: false,
                    IsVisible: true,
                    DesktopLeft: 420,
                    DesktopTop: 260,
                    Width: 120,
                    Height: 44,
                    FrameLeft: 320,
                    FrameTop: 180,
                    Capabilities: ["Invoke"],
                    Children: Array.Empty<string>()),
                new UnifiedStructuredSceneNode(
                    Id: "root:cancel",
                    ParentId: "root",
                    Depth: 1,
                    Role: "Button",
                    Name: "Cancel",
                    AutomationId: "cancelButton",
                    ClassName: "Button",
                    IsEnabled: true,
                    IsFocused: false,
                    IsVisible: true,
                    DesktopLeft: 560,
                    DesktopTop: 260,
                    Width: 120,
                    Height: 44,
                    FrameLeft: 460,
                    FrameTop: 180,
                    Capabilities: ["Invoke"],
                    Children: Array.Empty<string>())
            ],
            Source: "acceptance",
            Detail: "acceptance");

        var result = resolver.ResolveInteractiveTarget(
            graph,
            "Save",
            new HashSet<string>(
                ["Invoke"],
                StringComparer.OrdinalIgnoreCase));

        Require(
            result.Resolved &&
            result.Status == StructuredResolutionStatus.Resolved &&
            result.Node?.Id == "root:save" &&
            result.Score >= 100,
            "Structured resolver chưa chọn đúng target duy nhất.");
    }

    private static void CheckStructuredResolverRejectsAmbiguousTarget()
    {
        var resolver = new StructuredDesktopResolver();

        var graph = new UnifiedStructuredSceneGraph(
            WindowId: "0x1234",
            WindowTitle: "Acceptance",
            ProcessName: "acceptance",
            RootId: "root",
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Nodes:
            [
                new UnifiedStructuredSceneNode(
                    Id: "root:one",
                    ParentId: "root",
                    Depth: 1,
                    Role: "Button",
                    Name: "Open",
                    AutomationId: "openOne",
                    ClassName: "Button",
                    IsEnabled: true,
                    IsFocused: false,
                    IsVisible: true,
                    DesktopLeft: 100,
                    DesktopTop: 100,
                    Width: 100,
                    Height: 40,
                    FrameLeft: 100,
                    FrameTop: 100,
                    Capabilities: ["Invoke"],
                    Children: Array.Empty<string>()),
                new UnifiedStructuredSceneNode(
                    Id: "root:two",
                    ParentId: "root",
                    Depth: 1,
                    Role: "Button",
                    Name: "Open",
                    AutomationId: "openTwo",
                    ClassName: "Button",
                    IsEnabled: true,
                    IsFocused: false,
                    IsVisible: true,
                    DesktopLeft: 240,
                    DesktopTop: 100,
                    Width: 100,
                    Height: 40,
                    FrameLeft: 240,
                    FrameTop: 100,
                    Capabilities: ["Invoke"],
                    Children: Array.Empty<string>())
            ],
            Source: "acceptance",
            Detail: "acceptance");

        var result = resolver.ResolveInteractiveTarget(
            graph,
            "Open",
            new HashSet<string>(
                ["Invoke"],
                StringComparer.OrdinalIgnoreCase));

        Require(
            !result.Resolved &&
            result.Status == StructuredResolutionStatus.Ambiguous &&
            result.Node is null,
            "Structured resolver vẫn tự đoán khi có nhiều target cùng mức tin cậy.");
    }

    private static void CheckStructuredSceneGraphBuildsHierarchy()
    {
        var foreground = new ComputerWindowInfo(
            "0x1234",
            "Acceptance",
            "acceptance",
            42,
            true,
            100,
            80,
            900,
            700);

        var root = new StructuredDesktopNode(
            Token: "root",
            ParentToken: string.Empty,
            Depth: 0,
            Role: "Window",
            Name: "Acceptance",
            AutomationId: string.Empty,
            ClassName: "Window",
            IsEnabled: true,
            IsFocused: false,
            IsOffscreen: false,
            Left: 100,
            Top: 80,
            Width: 900,
            Height: 700,
            Patterns: Array.Empty<string>());

        var button = new StructuredDesktopNode(
            Token: "root:save",
            ParentToken: "root",
            Depth: 1,
            Role: "Button",
            Name: "Save",
            AutomationId: "saveButton",
            ClassName: "Button",
            IsEnabled: true,
            IsFocused: true,
            IsOffscreen: false,
            Left: 420,
            Top: 260,
            Width: 120,
            Height: 44,
            Patterns: ["Invoke", "Invoke"]);

        var snapshot = new StructuredDesktopSnapshot(
            WindowId: foreground.WindowId,
            RootToken: "root",
            CapturedAtUtc: DateTimeOffset.UtcNow,
            NodeCount: 2,
            MaximumNodes: 200,
            MaximumDepth: 6,
            Nodes: [root, button],
            Source: "flaui-uia3",
            Detail: "acceptance");

        var graph = UnifiedStructuredSceneGraphBuilder.Build(
            snapshot,
            foreground,
            frameLeft: 100,
            frameTop: 80,
            frameWidth: 900,
            frameHeight: 700);

        var rootNode = graph?.Find("root");
        var buttonNode = graph?.Find("root:save");

        Require(
            graph is not null &&
            graph.NodeCount == 2 &&
            graph.InteractiveNodeCount == 1 &&
            rootNode is not null &&
            rootNode.Children.SequenceEqual(["root:save"]) &&
            buttonNode is not null &&
            buttonNode.ParentId == "root" &&
            buttonNode.FrameLeft == 320 &&
            buttonNode.FrameTop == 180 &&
            buttonNode.Capabilities.SequenceEqual(["Invoke"]) &&
            buttonNode.Interactive,
            "Structured Scene Graph chưa giữ đúng hierarchy, geometry hoặc capability.");
    }

    private static void CheckUnifiedDesktopStateIncludesStructuredScene()
    {
        var node =
            new StructuredDesktopNode(
                Token: "root:button",
                ParentToken: "root",
                Depth: 1,
                Role: "Button",
                Name: "Save",
                AutomationId: "saveButton",
                ClassName: "Button",
                IsEnabled: true,
                IsFocused: false,
                IsOffscreen: false,
                Left: 100,
                Top: 120,
                Width: 80,
                Height: 32,
                Patterns: ["Invoke"]);

        var structured =
            new StructuredDesktopSnapshot(
                WindowId: "0x1234",
                RootToken: "root",
                CapturedAtUtc: DateTimeOffset.UtcNow,
                NodeCount: 1,
                MaximumNodes: 200,
                MaximumDepth: 6,
                Nodes: [node],
                Source: "flaui-uia3",
                Detail: "acceptance");

        var state =
            new ComputerOperatorDesktopState(
                DateTimeOffset.UtcNow,
                ForegroundWindow: null,
                Windows: Array.Empty<ComputerWindowInfo>(),
                FrameLeft: 0,
                FrameTop: 0,
                FrameWidth: 1920,
                FrameHeight: 1080,
                CaptureScope: "virtual-desktop",
                CaptureWindowId: null,
                CaptureWindowWasForeground: false,
                StructuredScene: structured);

        var summary =
            state.ToPromptSummary();

        Require(
            state.StructuredScene?.NodeCount == 1 &&
            summary.Contains(
                "role=Button",
                StringComparison.Ordinal) &&
            summary.Contains(
                "name=Save",
                StringComparison.Ordinal) &&
            summary.Contains(
                "patterns=Invoke",
                StringComparison.Ordinal),
            "Unified Desktop State chưa công bố structured UI scene cho planner.");
    }

    private static void CheckCapabilityCacheAvoidsRepeatedDiscovery()
    {
        var raw =
            new AcceptanceRawCapabilityDiscoveryService();

        var cache =
            new UniversalCapabilityCache();

        var cached =
            new CachedUniversalCapabilityDiscoveryService(
                raw,
                cache);

        _ = cached.Discover();
        _ = cached.Discover();

        var status =
            cache.GetStatus();

        Require(
            raw.DiscoverCount == 1 &&
            status.Hits >= 1 &&
            status.RuntimeSnapshotCached,
            "Capability cache chưa tránh repeated raw discovery.");
    }

    private static void CheckCapabilityCacheInvalidationForcesRefresh()
    {
        var raw =
            new AcceptanceRawCapabilityDiscoveryService();

        var cache =
            new UniversalCapabilityCache();

        var cached =
            new CachedUniversalCapabilityDiscoveryService(
                raw,
                cache);

        _ = cached.Discover();
        cache.InvalidateRuntime(
            "acceptance state changed");
        _ = cached.Discover();

        Require(
            raw.DiscoverCount == 2 &&
            cache.GetStatus().Generation >= 1,
            "Capability cache invalidate chưa buộc refresh.");
    }

    private static void CheckCapabilityCacheHealthOverride()
    {
        var raw =
            new AcceptanceRawCapabilityDiscoveryService();

        var cache =
            new UniversalCapabilityCache();

        var cached =
            new CachedUniversalCapabilityDiscoveryService(
                raw,
                cache);

        cache.MarkTemporarilyUnavailable(
            "browser.playwright-dom",
            TimeSpan.FromSeconds(5),
            "acceptance failure");

        var snapshot =
            cached.Discover();

        var playwright =
            snapshot.Signals.Single(item =>
                item.Key ==
                    "browser.playwright-dom");

        Require(
            !playwright.Available &&
            playwright.ConfidenceBonus == 0 &&
            cache.GetStatus()
                .TemporaryUnavailabilityCount >= 1,
            "Capability health override chưa tạm ngừng adapter lỗi.");
    }

    private static void CheckResilienceStopsOnPermissionDenial()
    {
        var classifier = new UniversalFailureClassifier();
        var result = classifier.Classify(
            new UnauthorizedAccessException("permission denied"),
            sideEffectMayHaveOccurred: false);

        Require(
            result.Action == UniversalFailureActions.Stop &&
            !result.RetryAllowed,
            "Permission denial vẫn đang được retry.");
    }

    private static void CheckResilienceRequiresVerificationAfterPossibleSideEffect()
    {
        var classifier = new UniversalFailureClassifier();
        var result = classifier.Classify(
            new TimeoutException("timeout"),
            sideEffectMayHaveOccurred: true);

        Require(
            result.Action == UniversalFailureActions.VerifyBeforeRetry &&
            result.RequiresVerificationBeforeAnotherSideEffect &&
            !result.RetryAllowed,
            "Lỗi sau side effect chưa buộc verify-before-retry.");
    }

    private static void CheckResilienceRetriesTransientReadOnlyFailure()
    {
        var classifier = new UniversalFailureClassifier();
        var executor = new UniversalResilienceExecutor(classifier);
        var calls = 0;

        var result = executor.ExecuteReadOnly(
            "acceptance-read",
            () =>
            {
                calls++;
                if (calls == 1)
                    throw new IOException("technical transient");

                return "ok";
            });

        Require(
            result == "ok" &&
            calls == 2,
            "Read-only transient failure chưa retry đúng một lần.");
    }

    private static void CheckOperatorCheckpointRecoversInterruptedRun()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "personalai-checkpoint-acceptance-" + Guid.NewGuid().ToString("N"));

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var workspace =
                new AcceptanceCheckpointWorkspaceContext(
                    "personal");

            var first =
                new SqliteComputerOperatorCheckpointStore(
                    configuration,
                    workspace,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SqliteComputerOperatorCheckpointStore>.Instance);

            var checkpoint =
                first.StartOrResume(
                    "Mở ứng dụng thử nghiệm");

            first.SaveProgress(
                checkpoint,
                ["Ứng dụng đã mở"],
                "Kiểm tra cửa sổ chính",
                0.50,
                "click-left",
                "Ứng dụng bắt đầu mở");

            var afterRestart =
                new SqliteComputerOperatorCheckpointStore(
                    configuration,
                    workspace,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SqliteComputerOperatorCheckpointStore>.Instance);

            var recovered =
                afterRestart.FindResumable(
                    "Mở ứng dụng thử nghiệm");

            Require(
                recovered is not null &&
                recovered.Status ==
                    ComputerOperatorCheckpointStatuses.Interrupted &&
                recovered.VerifiedMilestones.Contains(
                    "Ứng dụng đã mở",
                    StringComparer.OrdinalIgnoreCase),
                "Checkpoint không phục hồi interrupted state hoặc làm mất verified milestone.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void CheckOperatorCheckpointWorkspaceIsolation()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "personalai-checkpoint-workspace-" + Guid.NewGuid().ToString("N"));

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var personal =
                new AcceptanceCheckpointWorkspaceContext(
                    "personal");

            var work =
                new AcceptanceCheckpointWorkspaceContext(
                    "work");

            var personalStore =
                new SqliteComputerOperatorCheckpointStore(
                    configuration,
                    personal,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SqliteComputerOperatorCheckpointStore>.Instance);

            _ = personalStore.StartOrResume(
                "Mở ứng dụng thử nghiệm");

            var workStore =
                new SqliteComputerOperatorCheckpointStore(
                    configuration,
                    work,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SqliteComputerOperatorCheckpointStore>.Instance);

            var leaked =
                workStore.FindResumable(
                    "Mở ứng dụng thử nghiệm");

            Require(
                leaked is null,
                "Checkpoint đã bị đọc xuyên workspace.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void CheckOperatorCheckpointCompletedIsNotResumable()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "personalai-checkpoint-complete-" + Guid.NewGuid().ToString("N"));

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var workspace =
                new AcceptanceCheckpointWorkspaceContext(
                    "personal");

            var store =
                new SqliteComputerOperatorCheckpointStore(
                    configuration,
                    workspace,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SqliteComputerOperatorCheckpointStore>.Instance);

            var checkpoint =
                store.StartOrResume(
                    "Mở ứng dụng thử nghiệm");

            store.MarkStatus(
                checkpoint,
                ComputerOperatorCheckpointStatuses.Completed);

            Require(
                store.FindResumable(
                    "Mở ứng dụng thử nghiệm") is null,
                "Checkpoint completed vẫn đang được dùng để resume.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void CheckOperatorTelemetryDoesNotExposePayloads()
    {
        var telemetry =
            new ComputerOperatorTelemetry();

        using (var operation =
               telemetry.Begin(
                   ComputerOperatorTelemetryStages.GeminiPlan))
        {
            operation.Complete(
                success: true,
                route: "gemini");
        }

        var snapshot =
            telemetry.GetSnapshot();

        Require(
            snapshot.MemoryOnly &&
            snapshot.OpenTelemetryCompatible &&
            !snapshot.ContainsGoals &&
            !snapshot.ContainsTextPayloads &&
            !snapshot.ContainsScreenshots &&
            !snapshot.ContainsCoordinates,
            "Telemetry đang công bố hoặc lưu loại payload không được phép.");
    }

    private static void CheckOperatorTelemetrySanitizesUnsafeDimensions()
    {
        var telemetry =
            new ComputerOperatorTelemetry();

        using (var operation =
               telemetry.Begin(
                   "đây là nội dung task bí mật",
                   "Xin chào người dùng"))
        {
            operation.Complete(
                success: true,
                route: "route có khoảng trắng");
        }

        var item =
            telemetry.GetSnapshot()
                .RecentEvents
                .Single();

        Require(
            item.Stage == "other" &&
            item.Action == "other" &&
            item.Route == "other",
            "Telemetry chưa chặn dimension có thể chứa nội dung tự do.");
    }

    private static void CheckOperatorTelemetryAggregatesRoutes()
    {
        var telemetry =
            new ComputerOperatorTelemetry();

        using (var structured =
               telemetry.Begin(
                   ComputerOperatorTelemetryStages.GeminiPlan,
                   "structured-invoke"))
        {
            structured.Complete(
                success: true,
                route: "structured-first");
        }

        using (var gemini =
               telemetry.Begin(
                   ComputerOperatorTelemetryStages.GeminiPlan,
                   "click-left"))
        {
            gemini.Complete(
                success: true,
                route: "gemini");
        }

        using (var verify =
               telemetry.Begin(
                   ComputerOperatorTelemetryStages.StructuredVerify,
                   "structured-toggle"))
        {
            verify.Complete(
                success: true,
                route: "verified");
        }

        var snapshot =
            telemetry.GetSnapshot();

        Require(
            snapshot.RouteAggregates.Any(item =>
                item.Stage ==
                    ComputerOperatorTelemetryStages.GeminiPlan &&
                item.Route == "structured-first" &&
                item.Count == 1) &&
            snapshot.RouteAggregates.Any(item =>
                item.Stage ==
                    ComputerOperatorTelemetryStages.GeminiPlan &&
                item.Route == "gemini" &&
                item.Count == 1) &&
            snapshot.RouteAggregates.Any(item =>
                item.Stage ==
                    ComputerOperatorTelemetryStages.StructuredVerify &&
                item.Route == "verified" &&
                item.Count == 1),
            "Telemetry route aggregate chưa tách được structured-first, Gemini và structured verification.");
    }

    private static void CheckForensicCycleSummaryCoreFields()
    {
        var trace =
            new ComputerOperatorCycleTrace(
                7,
                "Mở ứng dụng rồi thao tác")
            {
                SceneId = "ABC123",
                CurrentSubgoal = "Chờ client mở",
                BeforeForeground = "old/window",
                AfterForeground = "new/window",
                WindowDelta = "appeared=[Client]; disappeared=[]; moved=[]; count=1->2",
                PlannerRoute = "gemini",
                Action = "click-left",
                Target = "Client",
                TargetElementId = "-",
                TargetBox = "10,20,100,40",
                DecisionConfidence = 0.91,
                ExpectedEffect = "Ứng dụng bắt đầu khởi chạy",
                DecisionReason = "Click kết quả tìm kiếm",
                Executed = true,
                ExecutionMilliseconds = 84,
                Executor = "computer-input",
                Verification = "Verified: launch transition",
                VerificationConfidence = 0.96,
                RecoveryCode = "none",
                RecoveryDetail = "-",
                Result = "verified",
                Next = "observe-next-cycle"
            };

        trace.PlannerTrace.Add(
            "Structured/Local=NotResolved");
        trace.PlannerTrace.Add(
            "Gemini=Called");
        trace.Evidence.Add(
            "frameDelta=0.1482");
        trace.Evidence.Add(
            "foregroundChanged=true");

        var summary =
            trace.RenderSummary();

        Require(
            summary.Contains(
                "[SUMMARY][CYCLE 7]",
                StringComparison.Ordinal) &&
            summary.Contains(
                "PlannerRoute: gemini",
                StringComparison.Ordinal) &&
            summary.Contains(
                "Decision: action=click-left",
                StringComparison.Ordinal) &&
            summary.Contains(
                "Execution: applied=True",
                StringComparison.Ordinal) &&
            summary.Contains(
                "Evidence:",
                StringComparison.Ordinal) &&
            summary.Contains(
                "Recovery: code=none",
                StringComparison.Ordinal) &&
            summary.Contains(
                "Next: observe-next-cycle",
                StringComparison.Ordinal),
            "Forensic cycle summary đang thiếu trường cốt lõi.");
    }

    private static void CheckForensicWindowDelta()
    {
        var before =
            new[]
            {
                new ComputerWindowInfo(
                    "1",
                    "A",
                    "a",
                    1,
                    true,
                    0,
                    0,
                    100,
                    100),
                new ComputerWindowInfo(
                    "2",
                    "B",
                    "b",
                    2,
                    false,
                    200,
                    0,
                    100,
                    100)
            };

        var after =
            new[]
            {
                new ComputerWindowInfo(
                    "1",
                    "A",
                    "a",
                    1,
                    false,
                    10,
                    10,
                    120,
                    100),
                new ComputerWindowInfo(
                    "3",
                    "C",
                    "c",
                    3,
                    true,
                    400,
                    0,
                    100,
                    100)
            };

        var delta =
            ComputerOperatorCycleTrace.DescribeWindowDelta(
                before,
                after);

        Require(
            delta.Contains(
                "appeared=[C]",
                StringComparison.Ordinal) &&
            delta.Contains(
                "disappeared=[B]",
                StringComparison.Ordinal) &&
            delta.Contains(
                "moved=[A]",
                StringComparison.Ordinal) &&
            delta.Contains(
                "count=2->2",
                StringComparison.Ordinal),
            "Forensic window delta chưa mô tả đủ appeared/disappeared/moved.");
    }

    private static void CheckAttachmentHybridRouting()
    {
        var image =
            ChatAttachmentStore.SelectRouteForAcceptance(
                ".png",
                "image",
                9 * 1024 * 1024);

        var xlsx =
            ChatAttachmentStore.SelectRouteForAcceptance(
                ".xlsx",
                "document",
                200 * 1024);

        var pptx =
            ChatAttachmentStore.SelectRouteForAcceptance(
                ".pptx",
                "document",
                200 * 1024);

        var codeSmall =
            ChatAttachmentStore.SelectRouteForAcceptance(
                ".cs",
                "text",
                64 * 1024);

        var codeLarge =
            ChatAttachmentStore.SelectRouteForAcceptance(
                ".cs",
                "text",
                2 * 1024 * 1024);

        Require(
            image == "direct" &&
            xlsx == "knowledge" &&
            pptx == "knowledge" &&
            codeSmall == "direct" &&
            codeLarge == "knowledge",
            "Hybrid attachment router không giữ đúng policy direct/RAG.");
    }

    private static void CheckOperatorTelemetryBoundsRecentEvents()
    {
        var telemetry =
            new ComputerOperatorTelemetry();

        for (var index = 0;
             index <
                 ComputerOperatorTelemetry.MaximumRecentEvents + 25;
             index++)
        {
            using var operation =
                telemetry.Begin(
                    ComputerOperatorTelemetryStages.Observe,
                    "frame");

            operation.Complete(
                success: true,
                route: "local");
        }

        var snapshot =
            telemetry.GetSnapshot();

        Require(
            snapshot.RecentEvents.Count ==
                ComputerOperatorTelemetry.MaximumRecentEvents &&
            snapshot.Aggregates
                .Single(item =>
                    item.Stage ==
                    ComputerOperatorTelemetryStages.Observe)
                .Count ==
                ComputerOperatorTelemetry.MaximumRecentEvents + 25,
            "Telemetry recent-event buffer hoặc aggregate count không đúng giới hạn.");
    }

    private static void CheckReliableOperatorRuntimeReadiness()
    {
        var coordinator =
            BuildReliableOperatorCoordinator(
                new UniversalCapabilitySnapshot(
                    PersonalAI.Web.Models.PersonalAiRelease.Version,
                    DateTimeOffset.UtcNow,
                    [
                        new UniversalCapabilitySignal(
                            "desktop.win32-accessibility",
                            ExecutionAgentChannels.Computer,
                            "Win32 Accessibility",
                            Available: true,
                            UniversalCapabilityReliability.Deterministic,
                            0.02,
                            Priority: 1,
                            "acceptance")
                    ]));

        var runtime =
            coordinator.GetRuntimeSnapshot();

        Require(
            runtime.Ready &&
            runtime.AvailableComputerCapabilities.Contains(
                "desktop.win32-accessibility",
                StringComparer.OrdinalIgnoreCase),
            "Reliable Operator chưa ready khi có desktop capability hợp lệ.");
    }

    private static void CheckReliableOperatorRuntimeNotReady()
    {
        var coordinator =
            BuildReliableOperatorCoordinator(
                new UniversalCapabilitySnapshot(
                    PersonalAI.Web.Models.PersonalAiRelease.Version,
                    DateTimeOffset.UtcNow,
                    [
                        new UniversalCapabilitySignal(
                            "desktop.flaui-uia3",
                            ExecutionAgentChannels.Computer,
                            "FlaUI",
                            Available: false,
                            UniversalCapabilityReliability.Structured,
                            0,
                            Priority: 1,
                            "acceptance unavailable")
                    ]));

        var runtime =
            coordinator.GetRuntimeSnapshot();

        Require(
            !runtime.Ready &&
            runtime.AvailableComputerCapabilities.Count == 0,
            "Reliable Operator vẫn ready dù không có desktop capability khả dụng.");
    }

    private static void CheckReliableOperatorAcceptsDeterministicWindowVerification()
    {
        var coordinator =
            BuildReliableOperatorCoordinator(
                AcceptanceDesktopCapabilitySnapshot());

        var decision =
            BuildClickDecision("virtual-desktop") with
            {
                Action = "focus-window",
                ExpectedEffect = "Cửa sổ đích ở foreground."
            };

        var result =
            coordinator.EvaluateLocalVerification(
                decision,
                new DesktopVerificationRoutingResult(
                    DesktopVerificationRoute.LocalVerified,
                    0.97,
                    "Foreground window đã thay đổi."));

        Require(
            result.Verified &&
            !result.RequiresSemanticVerification,
            "Verifier cửa sổ deterministic chưa được Reliable Operator chấp nhận.");
    }

    private static void CheckReliableOperatorRequiresSemanticForStrongLocal()
    {
        var coordinator =
            BuildReliableOperatorCoordinator(
                AcceptanceDesktopCapabilitySnapshot());

        var decision =
            BuildClickDecision("virtual-desktop") with
            {
                Action = "scroll",
                ExpectedEffect = "Nội dung đã cuộn."
            };

        var local =
            new DesktopVerificationRoutingResult(
                DesktopVerificationRoute.LocalVerified,
                0.94,
                "Frame thay đổi sau thao tác cuộn.");

        var localResult =
            coordinator.EvaluateLocalVerification(
                decision,
                local);

        Require(
            !localResult.Verified &&
            localResult.RequiresSemanticVerification,
            "Strong-local đơn lẻ đang tự complete mà chưa có semantic corroboration.");

        var semantic =
            coordinator.EvaluateSemanticVerification(
                decision,
                local,
                semanticPassed: true,
                semanticConfidence: 0.95,
                semanticReason: "Vision xác nhận nội dung đã cuộn.");

        Require(
            semantic.Verified,
            "Strong-local + semantic cùng kết luận nhưng Evidence Fusion chưa verify.");
    }

    private static UniversalReliableOperatorCoordinator BuildReliableOperatorCoordinator(
        UniversalCapabilitySnapshot snapshot) =>
        new(
            new AcceptanceCapabilityDiscoveryService(snapshot),
            new UniversalCapabilityCache(),
            new UniversalEvidenceFusionEngine(),
            new UniversalResilienceExecutor(
                new UniversalFailureClassifier()));

    private static UniversalCapabilitySnapshot AcceptanceDesktopCapabilitySnapshot() =>
        new(
            PersonalAI.Web.Models.PersonalAiRelease.Version,
            DateTimeOffset.UtcNow,
            [
                new UniversalCapabilitySignal(
                    "desktop.win32-accessibility",
                    ExecutionAgentChannels.Computer,
                    "Win32 Accessibility",
                    Available: true,
                    UniversalCapabilityReliability.Deterministic,
                    0.02,
                    Priority: 1,
                    "acceptance")
            ]);

    private static void CheckOperatorCheckpointRedactsSensitiveContext()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "personalai-checkpoint-sensitive-" + Guid.NewGuid().ToString("N"));

        try
        {
            var configuration =
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Tasks:Root"] = root
                        })
                    .Build();

            var workspace =
                new AcceptanceCheckpointWorkspaceContext(
                    "personal");

            var store =
                new SqliteComputerOperatorCheckpointStore(
                    configuration,
                    workspace,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SqliteComputerOperatorCheckpointStore>.Instance);

            const string rawGoal =
                "Mở ứng dụng thử nghiệm bình thường";

            var checkpoint =
                store.StartOrResume(rawGoal);

            var updated =
                store.SaveProgress(
                    checkpoint,
                    ["OTP 123456 đã xuất hiện"],
                    "Nhập access token abc123",
                    0.50,
                    "type password secret-value",
                    "verification code 654321 đã được nhập");

            Require(
                !updated.Goal.Equals(
                    rawGoal,
                    StringComparison.Ordinal) &&
                updated.VerifiedMilestones.All(item =>
                    !item.Contains(
                        "123456",
                        StringComparison.Ordinal)) &&
                !updated.CurrentSubgoal.Contains(
                    "abc123",
                    StringComparison.Ordinal) &&
                (updated.LastVerifiedAction is null ||
                 !updated.LastVerifiedAction.Contains(
                     "secret-value",
                     StringComparison.Ordinal)) &&
                (updated.LastVerifiedExpectedEffect is null ||
                 !updated.LastVerifiedExpectedEffect.Contains(
                     "654321",
                     StringComparison.Ordinal)),
                "Checkpoint vẫn lưu plaintext goal hoặc context nhạy cảm.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
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

    private static void CheckAgentOutcomeVerificationUsesUniversalEvidenceAdapter()
    {
        var capabilityRegistry =
            new ToolCapabilityRegistry(
                new ToolRegistry(
                    Array.Empty<IPersonalAiTool>()));

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
                agentRegistry,
                new AcceptanceVerificationEvidenceAdapters(
                    new UniversalOutcomeEvidence(
                        "acceptance-universal-adapter",
                        Passed: true,
                        Confidence: 0.91,
                        "Adapter xác minh độc lập.")));

        var result = verifier.VerifyAgent(
            new UniversalAgentOutcomeVerificationRequest(
                "Đọc trang đích",
                new ExecutionAgentResult(
                    "execution.browser.acceptance",
                    AgentExecutionStatuses.Succeeded,
                    "execution ok",
                    ["raw-agent-evidence"],
                    ChangedExternalState: true,
                    Verified: true,
                    Provider: "acceptance",
                    Model: "acceptance")));

        Require(
            result.Status == UniversalOutcomeStatuses.Verified &&
            result.GoalAchieved &&
            result.IndependentlyVerified &&
            result.Source == "acceptance-universal-adapter" &&
            Math.Abs(result.Confidence - 0.91) < 0.001,
            "Agent outcome verifier chưa dùng universal evidence adapter làm nguồn quyết định.");
    }

    private static void CheckAgentOutcomeVerificationRequiresEvidence()
    {
        var capabilityRegistry =
            new ToolCapabilityRegistry(
                new ToolRegistry(
                    Array.Empty<IPersonalAiTool>()));

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
                agentRegistry,
                new AcceptanceVerificationEvidenceAdapters(
                    agentEvidence: null));

        var result = verifier.VerifyAgent(
            new UniversalAgentOutcomeVerificationRequest(
                "Đọc trang đích",
                new ExecutionAgentResult(
                    "execution.browser.acceptance",
                    AgentExecutionStatuses.Succeeded,
                    "execution ok nhưng chưa verified",
                    ["raw-agent-evidence"],
                    ChangedExternalState: true,
                    Verified: false,
                    Provider: "acceptance",
                    Model: "acceptance")));

        Require(
            result.Status == UniversalOutcomeStatuses.NeedsVerification &&
            !result.GoalAchieved &&
            !result.IndependentlyVerified,
            "Agent success chưa có universal evidence nhưng vẫn bị coi là goal success.");
    }

    private static void CheckAgentOutcomeVerificationRejectsUnsupportedContract()
    {
        var capabilityRegistry =
            new ToolCapabilityRegistry(
                new ToolRegistry(
                    Array.Empty<IPersonalAiTool>()));

        var agentRegistry =
            new ExecutionAgentRegistry(
                new IExecutionAgent[]
                {
                    new AcceptanceExecutionAgent(
                        "execution.browser.no-verification",
                        ExecutionAgentChannels.Browser,
                        supportsVerification: false)
                });

        var verifier =
            new UniversalOutcomeVerificationService(
                capabilityRegistry,
                agentRegistry,
                new AcceptanceVerificationEvidenceAdapters(
                    new UniversalOutcomeEvidence(
                        "acceptance-universal-adapter",
                        Passed: true,
                        Confidence: 0.99,
                        "Không được dùng vì contract không hỗ trợ verification.")));

        var result = verifier.VerifyAgent(
            new UniversalAgentOutcomeVerificationRequest(
                "Đọc trang đích",
                new ExecutionAgentResult(
                    "execution.browser.no-verification",
                    AgentExecutionStatuses.Succeeded,
                    "execution ok",
                    ["raw-agent-evidence"],
                    ChangedExternalState: true,
                    Verified: true,
                    Provider: "acceptance",
                    Model: "acceptance")));

        Require(
            result.Status == UniversalOutcomeStatuses.NeedsVerification &&
            !result.GoalAchieved,
            "Agent contract không hỗ trợ verification nhưng vẫn được complete.");
    }

    private static void CheckExecutionLifecycleCompletesOnlyAfterVerification()
    {
        var agents =
            new ExecutionAgentRegistry(
                new IExecutionAgent[]
                {
                    new AcceptanceExecutionAgent(
                        "execution.browser.lifecycle",
                        ExecutionAgentChannels.Browser)
                });

        var router =
            new UniversalTaskRouter(
                agents,
                new ExecutionGateway(agents));

        var verifier =
            new UniversalOutcomeVerificationService(
                new ToolCapabilityRegistry(
                    new ToolRegistry(
                        Array.Empty<IPersonalAiTool>())),
                agents);

        var lifecycle =
            new UniversalExecutionLifecycleCoordinator(
                router,
                verifier);

        var result = lifecycle.ExecuteAsync(
                new UniversalExecutionLifecycleRequest(
                    "Mở https://example.com và xác minh trang",
                    PreferredChannel:
                        ExecutionAgentChannels.Browser,
                    ConfirmExecution: true))
            .GetAwaiter()
            .GetResult();

        Require(
            result.GoalComplete &&
            result.Action ==
                UniversalExecutionLifecycleActions.Complete &&
            result.Verification.Status ==
                UniversalOutcomeStatuses.Verified &&
            result.Verification.IndependentlyVerified,
            "Execution lifecycle complete trước hoặc không qua outcome verification.");
    }

    private static void CheckExecutionLifecycleBlocksMissingEvidence()
    {
        var agents =
            new ExecutionAgentRegistry(
                new IExecutionAgent[]
                {
                    new AcceptanceExecutionAgent(
                        "execution.browser.lifecycle-unverified",
                        ExecutionAgentChannels.Browser,
                        verifiedResult: false)
                });

        var router =
            new UniversalTaskRouter(
                agents,
                new ExecutionGateway(agents));

        var verifier =
            new UniversalOutcomeVerificationService(
                new ToolCapabilityRegistry(
                    new ToolRegistry(
                        Array.Empty<IPersonalAiTool>())),
                agents);

        var lifecycle =
            new UniversalExecutionLifecycleCoordinator(
                router,
                verifier);

        var result = lifecycle.ExecuteAsync(
                new UniversalExecutionLifecycleRequest(
                    "Mở https://example.com và xác minh trang",
                    PreferredChannel:
                        ExecutionAgentChannels.Browser,
                    ConfirmExecution: true))
            .GetAwaiter()
            .GetResult();

        Require(
            !result.GoalComplete &&
            result.Action ==
                UniversalExecutionLifecycleActions.VerifyOutcome &&
            result.Verification.Status ==
                UniversalOutcomeStatuses.NeedsVerification,
            "Execution lifecycle đã complete dù universal evidence còn thiếu.");
    }

    private static void CheckExecutionLifecycleStopsOnExecutionFailure()
    {
        var agents =
            new ExecutionAgentRegistry(
                new IExecutionAgent[]
                {
                    new AcceptanceExecutionAgent(
                        "execution.browser.lifecycle-failed",
                        ExecutionAgentChannels.Browser,
                        executionStatus:
                            AgentExecutionStatuses.Failed,
                        verifiedResult: false)
                });

        var router =
            new UniversalTaskRouter(
                agents,
                new ExecutionGateway(agents));

        var verifier =
            new UniversalOutcomeVerificationService(
                new ToolCapabilityRegistry(
                    new ToolRegistry(
                        Array.Empty<IPersonalAiTool>())),
                agents);

        var lifecycle =
            new UniversalExecutionLifecycleCoordinator(
                router,
                verifier);

        var result = lifecycle.ExecuteAsync(
                new UniversalExecutionLifecycleRequest(
                    "Mở https://example.com và xác minh trang",
                    PreferredChannel:
                        ExecutionAgentChannels.Browser,
                    ConfirmExecution: true))
            .GetAwaiter()
            .GetResult();

        Require(
            !result.GoalComplete &&
            result.Action ==
                UniversalExecutionLifecycleActions.Stop &&
            result.Verification.Status ==
                UniversalOutcomeStatuses.ExecutionFailed,
            "Execution lifecycle không stop khi execution agent fail.");
    }

    private static void CheckVerificationContinuationBrowserReadback()
    {
        var planner =
            new UniversalVerificationContinuationPlanner();

        var plan = planner.Plan(
            BuildAcceptanceRoute(
                ExecutionAgentChannels.Browser),
            new UniversalOutcomeVerificationResult(
                UniversalOutcomeStatuses.NeedsVerification,
                ExecutionSucceeded: true,
                GoalAchieved: false,
                IndependentlyVerified: false,
                Confidence: 0,
                Source: "acceptance",
                Reason: "missing evidence"));

        Require(
            plan.Required &&
            plan.Strategy ==
                UniversalVerificationStrategies.BrowserReadback &&
            plan.RequiresReadback &&
            !plan.RequiresExternalAi,
            "Browser continuation chưa ưu tiên structured readback.");
    }

    private static void CheckVerificationContinuationComputerObserveVerify()
    {
        var planner =
            new UniversalVerificationContinuationPlanner();

        var plan = planner.Plan(
            BuildAcceptanceRoute(
                ExecutionAgentChannels.Computer),
            new UniversalOutcomeVerificationResult(
                UniversalOutcomeStatuses.NeedsVerification,
                ExecutionSucceeded: true,
                GoalAchieved: false,
                IndependentlyVerified: false,
                Confidence: 0,
                Source: "acceptance",
                Reason: "missing evidence"));

        Require(
            plan.Required &&
            plan.Strategy ==
                UniversalVerificationStrategies.ComputerOperatorVerifier &&
            plan.RequiresReadback &&
            plan.RequiresExternalAi,
            "Computer continuation chưa route observe-verify đúng.");
    }

    private static void CheckVerificationContinuationSkipsVerifiedOutcome()
    {
        var planner =
            new UniversalVerificationContinuationPlanner();

        var plan = planner.Plan(
            BuildAcceptanceRoute(
                ExecutionAgentChannels.Browser),
            new UniversalOutcomeVerificationResult(
                UniversalOutcomeStatuses.Verified,
                ExecutionSucceeded: true,
                GoalAchieved: true,
                IndependentlyVerified: true,
                Confidence: 0.95,
                Source: "acceptance",
                Reason: "verified"));

        Require(
            !plan.Required &&
            plan.Strategy ==
                UniversalVerificationStrategies.None,
            "Verified outcome vẫn tạo continuation verification thừa.");
    }

    private static void CheckSafeBrowserContinuationCompletesAfterReadback()
    {
        var agents =
            new ExecutionAgentRegistry(
                new IExecutionAgent[]
                {
                    new AcceptanceExecutionAgent(
                        "execution.browser.safe-continuation",
                        ExecutionAgentChannels.Browser,
                        verifiedResult: false)
                });

        var router =
            new UniversalTaskRouter(
                agents,
                new ExecutionGateway(agents));

        var verifier =
            new UniversalOutcomeVerificationService(
                new ToolCapabilityRegistry(
                    new ToolRegistry(
                        Array.Empty<IPersonalAiTool>())),
                agents);

        var lifecycle =
            new UniversalExecutionLifecycleCoordinator(
                router,
                verifier,
                new UniversalVerificationContinuationPlanner(),
                new UniversalVerificationContinuationExecutor(
                    new AcceptanceBrowserAgentService(
                        "https://example.com",
                        "Example",
                        "Readable content")));

        var result = lifecycle.ExecuteAsync(
                new UniversalExecutionLifecycleRequest(
                    "Mở https://example.com",
                    PreferredChannel:
                        ExecutionAgentChannels.Browser,
                    ConfirmExecution: true))
            .GetAwaiter()
            .GetResult();

        Require(
            result.GoalComplete &&
            result.Action ==
                UniversalExecutionLifecycleActions.Complete &&
            result.Verification.Status ==
                UniversalOutcomeStatuses.Verified &&
            result.ContinuationExecution?.Evidence?.Source ==
                "browser-structured-readback",
            "Safe browser continuation chưa đưa lifecycle tới complete sau structured readback hợp lệ.");
    }

    private static void CheckSafeBrowserContinuationMismatchReplans()
    {
        var agents =
            new ExecutionAgentRegistry(
                new IExecutionAgent[]
                {
                    new AcceptanceExecutionAgent(
                        "execution.browser.safe-mismatch",
                        ExecutionAgentChannels.Browser,
                        verifiedResult: false)
                });

        var router =
            new UniversalTaskRouter(
                agents,
                new ExecutionGateway(agents));

        var verifier =
            new UniversalOutcomeVerificationService(
                new ToolCapabilityRegistry(
                    new ToolRegistry(
                        Array.Empty<IPersonalAiTool>())),
                agents);

        var lifecycle =
            new UniversalExecutionLifecycleCoordinator(
                router,
                verifier,
                new UniversalVerificationContinuationPlanner(),
                new UniversalVerificationContinuationExecutor(
                    new AcceptanceBrowserAgentService(
                        "https://wrong.example.com",
                        "Wrong",
                        "Wrong page")));

        var result = lifecycle.ExecuteAsync(
                new UniversalExecutionLifecycleRequest(
                    "Mở https://example.com",
                    PreferredChannel:
                        ExecutionAgentChannels.Browser,
                    ConfirmExecution: true))
            .GetAwaiter()
            .GetResult();

        Require(
            !result.GoalComplete &&
            result.Action ==
                UniversalExecutionLifecycleActions.ReplanGoal &&
            result.Verification.Status ==
                UniversalOutcomeStatuses.NotAchieved &&
            result.ContinuationExecution?.Evidence?.Passed == false,
            "Browser readback sai URL không buộc replan goal.");
    }

    private static void CheckSafeCodingContinuationStaysPending()
    {
        var executor =
            new UniversalVerificationContinuationExecutor(
                new AcceptanceBrowserAgentService(
                    "https://example.com",
                    "Example",
                    "Readable content"));

        var plan =
            new UniversalVerificationContinuationPlan(
                UniversalVerificationStrategies.CodingVerificationGate,
                ExecutionAgentChannels.Coding,
                Required: true,
                RequiresReadback: true,
                RequiresExternalAi: false,
                "acceptance");

        var result = executor.ExecuteAsync(
                plan,
                BuildAcceptanceRoute(
                    ExecutionAgentChannels.Coding),
                new ExecutionAgentResult(
                    "execution.coding.acceptance",
                    AgentExecutionStatuses.Succeeded,
                    "acceptance",
                    ["acceptance"],
                    ChangedExternalState: true,
                    Verified: false,
                    Provider: "acceptance",
                    Model: "acceptance"))
            .GetAwaiter()
            .GetResult();

        Require(
            result.Status ==
                UniversalVerificationContinuationStatuses.Pending &&
            result.Evidence is null,
            "Coding continuation thiếu target/repository context nhưng vẫn tự chạy verifier hoặc bịa evidence.");
    }

    private static void CheckCodingVerificationContextCompletesLifecycle()
    {
        var agents =
            new ExecutionAgentRegistry(
                new IExecutionAgent[]
                {
                    new AcceptanceExecutionAgent(
                        "execution.coding.context-pass",
                        ExecutionAgentChannels.Coding,
                        verifiedResult: false)
                });

        var router =
            new UniversalTaskRouter(
                agents,
                new ExecutionGateway(agents));

        var verifier =
            new UniversalOutcomeVerificationService(
                new ToolCapabilityRegistry(
                    new ToolRegistry(
                        Array.Empty<IPersonalAiTool>())),
                agents);

        var development =
            new AcceptanceDevelopmentAgentService();

        var lifecycle =
            new UniversalExecutionLifecycleCoordinator(
                router,
                verifier,
                new UniversalVerificationContinuationPlanner(),
                new UniversalVerificationContinuationExecutor(
                    new AcceptanceBrowserAgentService(
                        "https://example.com",
                        "Example",
                        "Readable content"),
                    new CodingVerificationGate(
                        development)));

        var result = lifecycle.ExecuteAsync(
                new UniversalExecutionLifecycleRequest(
                    "code test project",
                    PreferredChannel:
                        ExecutionAgentChannels.Coding,
                    ConfirmExecution: true,
                    VerificationContext:
                        new UniversalVerificationContinuationContext(
                            new CodingVerificationRequest(
                                "src/PersonalAI.Web/PersonalAI.Web.csproj",
                                "."))))
            .GetAwaiter()
            .GetResult();

        Require(
            result.GoalComplete &&
            result.Action ==
                UniversalExecutionLifecycleActions.Complete &&
            result.Verification.Status ==
                UniversalOutcomeStatuses.Verified &&
            result.ContinuationExecution?.Evidence?.Source ==
                "coding-verification-gate" &&
            development.RestoreCount == 1 &&
            development.BuildCount == 1 &&
            development.TestCount == 1 &&
            development.DiffCount == 1,
            "Coding context PASS chưa chạy đủ verification gate hoặc chưa complete lifecycle.");
    }

    private static void CheckCodingVerificationContextFailureReplans()
    {
        var agents =
            new ExecutionAgentRegistry(
                new IExecutionAgent[]
                {
                    new AcceptanceExecutionAgent(
                        "execution.coding.context-fail",
                        ExecutionAgentChannels.Coding,
                        verifiedResult: false)
                });

        var router =
            new UniversalTaskRouter(
                agents,
                new ExecutionGateway(agents));

        var verifier =
            new UniversalOutcomeVerificationService(
                new ToolCapabilityRegistry(
                    new ToolRegistry(
                        Array.Empty<IPersonalAiTool>())),
                agents);

        var development =
            new AcceptanceDevelopmentAgentService(
                failBuild: true);

        var lifecycle =
            new UniversalExecutionLifecycleCoordinator(
                router,
                verifier,
                new UniversalVerificationContinuationPlanner(),
                new UniversalVerificationContinuationExecutor(
                    new AcceptanceBrowserAgentService(
                        "https://example.com",
                        "Example",
                        "Readable content"),
                    new CodingVerificationGate(
                        development)));

        var result = lifecycle.ExecuteAsync(
                new UniversalExecutionLifecycleRequest(
                    "code test project",
                    PreferredChannel:
                        ExecutionAgentChannels.Coding,
                    ConfirmExecution: true,
                    VerificationContext:
                        new UniversalVerificationContinuationContext(
                            new CodingVerificationRequest(
                                "src/PersonalAI.Web/PersonalAI.Web.csproj",
                                "."))))
            .GetAwaiter()
            .GetResult();

        Require(
            !result.GoalComplete &&
            result.Action ==
                UniversalExecutionLifecycleActions.ReplanGoal &&
            result.Verification.Status ==
                UniversalOutcomeStatuses.NotAchieved &&
            result.ContinuationExecution?.Evidence?.Passed == false &&
            development.RestoreCount == 1 &&
            development.BuildCount == 1 &&
            development.TestCount == 0,
            "Coding context FAIL không dừng đúng gate hoặc chưa buộc replan.");
    }

    private static void CheckGenericTextEngineDirectSetAndVerify()
    {
        var backend =
            new AcceptanceTextAccessibilityBackend(
                initialValue: "old",
                failFirstSet: false,
                sensitive: false);

        var engine =
            new GenericTextInteractionEngine(
                backend,
                new AcceptanceTextClipboardWriter(),
                new AcceptanceComputerUseService());

        var result = engine.Execute(
            new TextInteractionRequest(
                AcceptanceComputerUseService.WindowId,
                "Xin chào"));

        Require(
            result.Verified &&
            result.Strategy ==
                TextInteractionStrategies.AccessibilityDirect &&
            result.ActualText == "Xin chào" &&
            backend.SetCount == 1,
            "Generic Text Engine chưa direct-set + local verify đúng.");
    }

    private static void CheckGenericTextEngineRepairsOnce()
    {
        var backend =
            new AcceptanceTextAccessibilityBackend(
                initialValue: "old",
                failFirstSet: true,
                sensitive: false);

        var engine =
            new GenericTextInteractionEngine(
                backend,
                new AcceptanceTextClipboardWriter(),
                new AcceptanceComputerUseService());

        var result = engine.Execute(
            new TextInteractionRequest(
                AcceptanceComputerUseService.WindowId,
                "Nội dung cuối cùng"));

        Require(
            result.Verified &&
            result.RepairAttempted &&
            backend.SetCount == 2 &&
            result.ActualText == "Nội dung cuối cùng",
            "Generic Text Engine chưa giới hạn repair về đúng một lần.");
    }

    private static void CheckGenericTextEngineRejectsSensitiveTarget()
    {
        var backend =
            new AcceptanceTextAccessibilityBackend(
                initialValue: string.Empty,
                failFirstSet: false,
                sensitive: true);

        var engine =
            new GenericTextInteractionEngine(
                backend,
                new AcceptanceTextClipboardWriter(),
                new AcceptanceComputerUseService());

        var rejected = false;
        try
        {
            _ = engine.Execute(
                new TextInteractionRequest(
                    AcceptanceComputerUseService.WindowId,
                    "secret"));
        }
        catch (ToolExecutionInputException)
        {
            rejected = true;
        }

        Require(
            rejected &&
            backend.SetCount == 0,
            "Generic Text Engine chưa chặn sensitive/password target.");
    }

    private static void CheckFlaUiCompositePrefersStructuredBackend()
    {
        var structured =
            new AcceptanceStructuredTextBackend(
                new TextTargetCapabilities(
                    AcceptanceComputerUseService.WindowId,
                    new nint(11),
                    "Edit",
                    IsFocused: true,
                    CanRead: true,
                    CanDirectSet: true,
                    SupportsSelection: false,
                    IsReadOnly: false,
                    IsSensitive: false,
                    "flaui-uia3",
                    "structured-token"));

        var fallback =
            new AcceptanceWin32TextBackend(
                new TextTargetCapabilities(
                    AcceptanceComputerUseService.WindowId,
                    new nint(22),
                    "Edit",
                    IsFocused: true,
                    CanRead: true,
                    CanDirectSet: true,
                    SupportsSelection: true,
                    IsReadOnly: false,
                    IsSensitive: false,
                    "win32-accessibility"));

        var composite =
            new CompositeTextAccessibilityBackend(
                structured,
                fallback);

        var result = composite.Probe(
            AcceptanceComputerUseService.WindowId);

        Require(
            result.Backend == "flaui-uia3" &&
            structured.ProbeCount == 1 &&
            fallback.ProbeCount == 0,
            "Composite chưa ưu tiên FlaUI khi structured evidence đủ mạnh.");
    }

    private static void CheckFlaUiCompositeFallsBackToWin32()
    {
        var structured =
            new AcceptanceStructuredTextBackend(
                new TextTargetCapabilities(
                    AcceptanceComputerUseService.WindowId,
                    nint.Zero,
                    string.Empty,
                    IsFocused: true,
                    CanRead: false,
                    CanDirectSet: false,
                    SupportsSelection: false,
                    IsReadOnly: false,
                    IsSensitive: false,
                    "flaui-uia3"));

        var fallback =
            new AcceptanceWin32TextBackend(
                new TextTargetCapabilities(
                    AcceptanceComputerUseService.WindowId,
                    new nint(22),
                    "Edit",
                    IsFocused: true,
                    CanRead: true,
                    CanDirectSet: true,
                    SupportsSelection: true,
                    IsReadOnly: false,
                    IsSensitive: false,
                    "win32-accessibility"));

        var composite =
            new CompositeTextAccessibilityBackend(
                structured,
                fallback);

        var result = composite.Probe(
            AcceptanceComputerUseService.WindowId);

        Require(
            result.Backend == "win32-accessibility" &&
            structured.ProbeCount == 1 &&
            fallback.ProbeCount == 1,
            "Composite chưa fallback Win32 khi FlaUI thiếu capability.");
    }

    private static void CheckFlaUiCompositePreservesSensitiveDetection()
    {
        var structured =
            new AcceptanceStructuredTextBackend(
                new TextTargetCapabilities(
                    AcceptanceComputerUseService.WindowId,
                    new nint(11),
                    "PasswordBox",
                    IsFocused: true,
                    CanRead: false,
                    CanDirectSet: false,
                    SupportsSelection: false,
                    IsReadOnly: false,
                    IsSensitive: true,
                    "flaui-uia3",
                    "sensitive-token"));

        var fallback =
            new AcceptanceWin32TextBackend(
                new TextTargetCapabilities(
                    AcceptanceComputerUseService.WindowId,
                    new nint(22),
                    "Edit",
                    IsFocused: true,
                    CanRead: true,
                    CanDirectSet: true,
                    SupportsSelection: true,
                    IsReadOnly: false,
                    IsSensitive: false,
                    "win32-accessibility"));

        var composite =
            new CompositeTextAccessibilityBackend(
                structured,
                fallback);

        var result = composite.Probe(
            AcceptanceComputerUseService.WindowId);

        Require(
            result.IsSensitive &&
            result.Backend == "flaui-uia3" &&
            fallback.ProbeCount == 0,
            "Composite đã làm mất sensitive evidence hoặc fallback không an toàn.");
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

    private sealed class AcceptanceOcrProvider(
        string name,
        DesktopOcrObservation observation)
        : IDesktopOcrProvider
    {
        public int CallCount { get; private set; }

        public string Name { get; } =
            name;

        public DesktopOcrObservation ReadWindow(
            string windowId)
        {
            CallCount++;
            return observation;
        }
    }

    private sealed class AcceptanceOcrSensor(
        DesktopOcrObservation observation)
        : IDesktopOcrSensor
    {
        public DesktopOcrObservation ReadWindow(
            string windowId,
            LocalVisualSensorBudget? budget = null) =>
            observation;
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

        private readonly string executionStatus;
        private readonly bool verifiedResult;

        public AcceptanceExecutionAgent(
            string id,
            string channel,
            bool supportsVerification = true,
            string executionStatus = AgentExecutionStatuses.Succeeded,
            bool verifiedResult = true)
        {
            this.executionStatus = executionStatus;
            this.verifiedResult = verifiedResult;

            Definition = new ExecutionAgentDefinition(
                id,
                id,
                "Acceptance execution agent.",
                ["acceptance"],
                [channel],
                HasSideEffects: true,
                RequiresExplicitInvocation: true,
                SupportsVerification: supportsVerification,
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
                    executionStatus,
                    "acceptance",
                    ["acceptance"],
                    ChangedExternalState: true,
                    Verified: verifiedResult,
                    Provider: "acceptance",
                    Model: "acceptance"));
        }
    }

    private sealed class AcceptanceRawCapabilityDiscoveryService
        : IRawUniversalCapabilityDiscoveryService
    {
        public int DiscoverCount { get; private set; }

        public UniversalCapabilitySnapshot Discover()
        {
            DiscoverCount++;

            return new(
                PersonalAiRelease.Version,
                DateTimeOffset.UtcNow,
                [
                    new UniversalCapabilitySignal(
                        "browser.playwright-dom",
                        ExecutionAgentChannels.Browser,
                        "Playwright + Microsoft Edge",
                        Available: true,
                        UniversalCapabilityReliability.Structured,
                        0.05,
                        Priority: 1,
                        "acceptance")
                ]);
        }
    }

    private sealed class AcceptanceObservationWakeSource(
        bool emitEvent)
        : IAdaptiveObservationWakeSource
    {
        public int WaitCount { get; private set; }

        public string? LastTargetWindowId { get; private set; }

        public bool EventDrivenAvailable => true;

        public DesktopEventObservationSnapshot GetSnapshot() =>
            new(
                EventDrivenAvailable: true,
                ReceivedEvents: emitEvent ? 1 : 0,
                CoalescedEvents: 0,
                DroppedEvents: 0,
                DeliveredWakeups: emitEvent ? WaitCount : 0,
                PollFallbacks: emitEvent ? 0 : WaitCount,
                QueuedEvents: 0,
                LastEvent: null);

        public Task<DesktopObservationWakeResult> WaitForWindowAsync(
            string? windowId,
            TimeSpan fallbackDelay,
            CancellationToken cancellationToken = default)
        {
            LastTargetWindowId =
                windowId;

            return WaitAsync(
                fallbackDelay,
                cancellationToken);
        }

        public async Task<DesktopObservationWakeResult> WaitAsync(
            TimeSpan fallbackDelay,
            CancellationToken cancellationToken = default)
        {
            WaitCount++;

            if (emitEvent)
            {
                await Task.Yield();
                return new(
                    EventReceived: true,
                    new DesktopSystemEvent(
                        DesktopSystemEventKinds.ForegroundChanged,
                        "0x1",
                        DateTimeOffset.UtcNow,
                        0x0003,
                        "acceptance event"),
                    TimeSpan.Zero,
                    "acceptance event");
            }

            await Task.Delay(
                fallbackDelay,
                cancellationToken);

            return new(
                EventReceived: false,
                Event: null,
                fallbackDelay,
                "acceptance fallback");
        }
    }

    private sealed class AcceptanceCheckpointWorkspaceContext(
        string workspaceId)
        : IWorkspaceContextAccessor
    {
        public string CurrentWorkspaceId { get; } =
            workspaceId;

        public PersonalAI.Web.Models.PersonalWorkspace CurrentWorkspace =>
            throw new NotSupportedException(
                "Acceptance checkpoint store chỉ cần CurrentWorkspaceId.");
    }

    private sealed class AcceptanceCapabilityDiscoveryService(
        UniversalCapabilitySnapshot snapshot)
        : IUniversalCapabilityDiscoveryService
    {
        public UniversalCapabilitySnapshot Discover() =>
            snapshot;
    }

    private sealed class AcceptanceStructuredTextBackend(
        TextTargetCapabilities capabilities)
        : IFlaUiTextAccessibilityBackend
    {
        public int ProbeCount { get; private set; }

        public TextTargetCapabilities Probe(
            string windowId)
        {
            ProbeCount++;
            return capabilities with
            {
                WindowId = windowId
            };
        }

        public string? TryRead(
            TextTargetCapabilities target) =>
            "structured";

        public bool TryDirectSet(
            TextTargetCapabilities target,
            string text,
            string writeMode) =>
            true;

        public bool IsSameFocusedTarget(
            TextTargetCapabilities target) =>
            true;
    }

    private sealed class AcceptanceWin32TextBackend(
        TextTargetCapabilities capabilities)
        : IWin32TextAccessibilityBackend
    {
        public int ProbeCount { get; private set; }

        public TextTargetCapabilities Probe(
            string windowId)
        {
            ProbeCount++;
            return capabilities with
            {
                WindowId = windowId
            };
        }

        public string? TryRead(
            TextTargetCapabilities target) =>
            "win32";

        public bool TryDirectSet(
            TextTargetCapabilities target,
            string text,
            string writeMode) =>
            true;

        public bool IsSameFocusedTarget(
            TextTargetCapabilities target) =>
            true;
    }

    private sealed class AcceptanceTextAccessibilityBackend(
        string initialValue,
        bool failFirstSet,
        bool sensitive)
        : ITextAccessibilityBackend
    {
        private string value = initialValue;

        public int SetCount { get; private set; }

        public TextTargetCapabilities Probe(
            string windowId) =>
            new(
                windowId,
                new nint(1),
                "Edit",
                IsFocused: true,
                CanRead: true,
                CanDirectSet: true,
                SupportsSelection: true,
                IsReadOnly: false,
                IsSensitive: sensitive,
                "acceptance-accessibility");

        public string? TryRead(
            TextTargetCapabilities target) =>
            value;

        public bool TryDirectSet(
            TextTargetCapabilities target,
            string text,
            string writeMode)
        {
            SetCount++;

            if (failFirstSet &&
                SetCount == 1)
            {
                value = text + "!";
                return true;
            }

            value = text;
            return true;
        }

        public bool IsSameFocusedTarget(
            TextTargetCapabilities target) =>
            true;
    }

    private sealed class AcceptanceTextClipboardWriter
        : ITextClipboardWriter
    {
        public bool CanUseSafely(
            out string reason)
        {
            reason = "acceptance";
            return false;
        }

        public bool TryPaste(
            TextTargetCapabilities target,
            string text,
            string writeMode,
            out string detail)
        {
            detail = "acceptance";
            return false;
        }
    }

    private sealed class AcceptanceVerificationEvidenceAdapters(
        UniversalOutcomeEvidence? agentEvidence)
        : IUniversalVerificationEvidenceAdapters
    {
        public UniversalOutcomeEvidence? FromAgent(
            ExecutionAgentResult result) =>
            agentEvidence;

        public UniversalOutcomeEvidence? FromBrowser(
            BrowserExecutionBackendResult result) =>
            throw new NotSupportedException();

        public UniversalOutcomeEvidence FromCoding(
            CodingVerificationReport report) =>
            throw new NotSupportedException();

        public UniversalOutcomeEvidence? FromDesktop(
            DesktopVerificationRoutingResult result) =>
            throw new NotSupportedException();
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

    private sealed class AcceptanceBrowserAgentService(
        string url,
        string title,
        string text)
        : IBrowserAgentService
    {
        public BrowserAgentStatusResponse GetStatus() =>
            new(
                PersonalAiRelease.Version,
                "acceptance",
                Supported: true,
                JavaScriptEnabled: false,
                CookiesEnabled: false,
                DownloadsEnabled: false,
                FormSubmissionEnabled: false,
                MaximumResponseBytes: 1024,
                MaximumPageTextCharacters: 8000,
                MaximumLinks: 30,
                MaximumRedirects: 0,
                [BrowserAgentCapabilities.PageObserve],
                []);

        public BrowserSessionInfo GetSessionInfo() =>
            new(
                "acceptance",
                HasPage: true,
                url,
                "https://example.com",
                title,
                200,
                "text/html",
                DateTimeOffset.UtcNow,
                text.Length,
                0,
                1);

        public Task<BrowserNavigationResult> NavigateAsync(
            string targetUrl,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BrowserNavigationResult> OpenLinkAsync(
            int linkIndex,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public BrowserPageObservation ObservePage(
            int maximumCharacters = BrowserAgentService.MaximumPageTextCharacters,
            int maximumLinks = BrowserAgentService.MaximumLinks) =>
            new(
                "acceptance",
                url,
                "https://example.com",
                title,
                200,
                "text/html",
                DateTimeOffset.UtcNow,
                text,
                TextTruncated: false,
                [],
                1);
    }

    private sealed class AcceptancePlaywrightBrowserExecutionBackend(
        double confidence,
        bool success,
        bool verified)
        : IPlaywrightBrowserExecutionBackend
    {
        public int CallCount { get; private set; }

        public string Engine => "acceptance-playwright";

        public bool CanHandle(
            ExecutionAgentRequest request,
            out double resolvedConfidence,
            out string reason)
        {
            var accepted =
                request.Channel.Equals(
                    ExecutionAgentChannels.Browser,
                    StringComparison.OrdinalIgnoreCase);

            resolvedConfidence =
                accepted ? confidence : 0;
            reason =
                accepted
                    ? "acceptance-playwright"
                    : "wrong-channel";
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
                    success,
                    "acceptance playwright",
                    ["dom"],
                    ChangedExternalState: success,
                    Verified: verified,
                    Engine));
        }
    }

    private sealed class AcceptanceHttpBrowserExecutionBackend(
        double confidence,
        bool success,
        bool verified)
        : IHttpBrowserExecutionBackend
    {
        public int CallCount { get; private set; }

        public string Engine => "acceptance-http";

        public bool CanHandle(
            ExecutionAgentRequest request,
            out double resolvedConfidence,
            out string reason)
        {
            var accepted =
                request.Channel.Equals(
                    ExecutionAgentChannels.Browser,
                    StringComparison.OrdinalIgnoreCase);

            resolvedConfidence =
                accepted ? confidence : 0;
            reason =
                accepted
                    ? "acceptance-http"
                    : "wrong-channel";
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
                    success,
                    "acceptance http",
                    ["http"],
                    ChangedExternalState: success,
                    Verified: verified,
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
    private sealed class AcceptanceLearnedTimingService(
        ComputerOperatorTimingProfile profile)
        : IComputerOperatorLearnedTimingService
    {
        public ComputerOperatorTimingProfile GetProfile(
            string stage,
            string action) =>
            profile;

        public IReadOnlyList<ComputerOperatorTimingProfile> GetProfiles() =>
            [profile];
    }

    private sealed class AcceptanceWorkspaceContextAccessor(
        string workspaceId)
        : IWorkspaceContextAccessor
    {
        public string CurrentWorkspaceId { get; } =
            workspaceId;

        public PersonalWorkspace CurrentWorkspace { get; } =
            new(
                workspaceId,
                "Acceptance",
                "Acceptance workspace",
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                Array.Empty<string>(),
                Array.Empty<string>());
    }

}
