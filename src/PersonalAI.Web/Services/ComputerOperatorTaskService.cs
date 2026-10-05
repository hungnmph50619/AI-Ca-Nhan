using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerOperatorTaskService
{
    Task<ComputerOperatorTaskResult> RunAsync(
        string goal,
        CancellationToken cancellationToken = default);
}

public sealed record ComputerOperatorTaskStep(
    int Index,
    string Action,
    string Detail);

public sealed record ComputerOperatorTaskResult(
    string Goal,
    bool Completed,
    string Summary,
    IReadOnlyList<ComputerOperatorTaskStep> Steps,
    string Provider,
    string Model);

public sealed record VerificationCaptureContext(
    string CaptureScope,
    string? WindowId,
    string? MonitorDevice,
    int Left,
    int Top,
    int Width,
    int Height,
    uint DpiX,
    uint DpiY)
{
    public static VerificationCaptureContext From(
        DesktopScreenshotFrame frame) =>
        new(
            frame.CaptureScope,
            frame.WindowId,
            frame.MonitorDevice,
            frame.Left,
            frame.Top,
            frame.Width,
            frame.Height,
            frame.MonitorDpiX,
            frame.MonitorDpiY);

    public bool Matches(
        DesktopScreenshotFrame frame) =>
        CaptureScope.Equals(
            frame.CaptureScope,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            WindowId,
            frame.WindowId,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            MonitorDevice,
            frame.MonitorDevice,
            StringComparison.OrdinalIgnoreCase) &&
        Left == frame.Left &&
        Top == frame.Top &&
        Width == frame.Width &&
        Height == frame.Height &&
        DpiX == frame.MonitorDpiX &&
        DpiY == frame.MonitorDpiY;
}

public sealed record ComputerOperatorDesktopState(
    DateTimeOffset CapturedAtUtc,
    ComputerWindowInfo? ForegroundWindow,
    IReadOnlyList<ComputerWindowInfo> Windows,
    int FrameLeft,
    int FrameTop,
    int FrameWidth,
    int FrameHeight,
    string CaptureScope,
    string? CaptureWindowId,
    bool CaptureWindowWasForeground)
{
    public string ToPromptSummary()
    {
        var lines = new List<string>
        {
            ForegroundWindow is null
                ? "Foreground: không xác định"
                : $"Foreground: id={ForegroundWindow.WindowId}; title={ForegroundWindow.Title}; process={ForegroundWindow.ProcessName ?? "?"}; rect={ForegroundWindow.Left},{ForegroundWindow.Top},{ForegroundWindow.Width},{ForegroundWindow.Height}",
            $"Observation frame: scope={CaptureScope}; origin={FrameLeft},{FrameTop}; size={FrameWidth}x{FrameHeight}; window={CaptureWindowId ?? "-"}; foreground={CaptureWindowWasForeground}",
            "Cửa sổ đang hiển thị:"
        };

        foreach (var window in Windows)
        {
            lines.Add(
                $"- id={window.WindowId}; title={window.Title}; process={window.ProcessName ?? "?"}; foreground={window.IsForeground}; rect={window.Left},{window.Top},{window.Width},{window.Height}");
        }

        return string.Join("\n", lines);
    }
}

public sealed class ComputerOperatorTaskService(
    IComputerUseService computer,
    ComputerControlGate control,
    IDesktopScreenshotService screenshots,
    DesktopVisionService vision,
    ComputerOperatorProgressStore progress,
    ComputerOperatorExecutionControl execution,
    IComputerCoordinateTransformService coordinates,
    IComputerDisplayTopologyService displays,
    IComputerSafeTargetingService targeting,
    IDesktopFrameDifferenceService frameDifferences,
    IDesktopLocalFastObserver fastObserver,
    IDesktopTemporalSceneService temporalScenes,
    IComputerOperatorActionExecutor actionExecutor,
    IGenericTextInteractionEngine textInteraction,
    IAdaptiveVerificationWaitEngine adaptiveWait,
    IComputerOperatorCheckpointStore checkpoints,
    IComputerOperatorTelemetry telemetry,
    IUniversalReliableOperatorCoordinator reliableOperator,
    ILogger<ComputerOperatorTaskService> logger)
    : IComputerOperatorTaskService
{
    private const int MaximumSteps = 64;
    private const int ScopedLeaseActions = 16;
    private const int ScopedLeaseSeconds = 120;
    private const int ScopedLeaseRenewalThresholdSeconds = 25;
    private const double MinimumConfidence = 0.72;
    private static readonly IDesktopVerificationRouter VerificationRouter =
        new DesktopVerificationRouter();
    private static readonly IDesktopRoiVisionService RoiVision =
        new DesktopRoiVisionService();
    private static readonly IAdaptiveGeminiCallPolicy GeminiCallPolicy =
        new AdaptiveGeminiCallPolicy();
    private static readonly IDesktopDynamicTargetTracker TargetTracker =
        new DesktopDynamicTargetTracker();
    private static readonly IComputerOperatorConfidenceEngine ConfidenceEngine =
        new ComputerOperatorConfidenceEngine();
    private static readonly IComputerOperatorFailureRecoveryEngine RecoveryEngine =
        new ComputerOperatorFailureRecoveryEngine();
    private static readonly IDesktopLocalActionPlanner LocalPlanner =
        new DesktopLocalActionPlanner();

    private static readonly string[] SecretTerms =
    [
        "password", "mật khẩu", "otp", "2fa", "mã xác thực",
        "verification code", "api key", "secret", "access token",
        "refresh token", "private key"
    ];

    public async Task<ComputerOperatorTaskResult> RunAsync(
        string goal,
        CancellationToken cancellationToken = default)
    {
        var normalizedGoal = (goal ?? string.Empty).Trim();
        if (normalizedGoal.Length is < 2 or > 1200)
            throw new ToolExecutionInputException(
                "Yêu cầu Computer Operator phải từ 2 đến 1200 ký tự.");

        if (SecretTerms.Any(term =>
                normalizedGoal.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase)))
            throw new ToolExecutionInputException(
                "Computer Operator không tự nhập mật khẩu, OTP, token, khóa hoặc bí mật.");

        if (!vision.Ready)
            throw new ToolExecutionInputException(
                "Computer Operator cần Desktop Vision/Gemini đã sẵn sàng.");

        using var taskTelemetry =
            telemetry.Begin(
                ComputerOperatorTelemetryStages.Task);

        control.EnableScopedAutomation(
            maximumActions: ScopedLeaseActions,
            maximumSeconds: ScopedLeaseSeconds);

        var operatorToken = execution.Begin(normalizedGoal);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            operatorToken);

        progress.Start(
            $"Bắt đầu tác vụ: {normalizedGoal}");

        var reliableRuntime =
            reliableOperator.GetRuntimeSnapshot();

        if (!reliableRuntime.Ready)
        {
            taskTelemetry.Complete(
                success: false,
                route: "runtime-not-ready");

            progress.Block(
                reliableRuntime.Reason);

            throw new ToolExecutionInputException(
                reliableRuntime.Reason);
        }

        progress.Add(
            "reliable-runtime",
            $"Universal Reliable Operator: {reliableRuntime.Reason}",
            "ready");

        var resumableCheckpoint =
            checkpoints.FindResumable(
                normalizedGoal);

        var checkpoint =
            checkpoints.StartOrResume(
                normalizedGoal);

        var steps = new List<ComputerOperatorTaskStep>();
        var taskHistory = new List<string>();
        var recovery = new ComputerOperatorRecoverySession();
        var loopGuard = new ComputerOperatorLoopGuardSession();
        var actionState = new ComputerOperatorActionStateMachine();
        var verifiedMilestones = new HashSet<string>(
            checkpoint.VerifiedMilestones,
            StringComparer.OrdinalIgnoreCase);
        var currentSubgoal =
            checkpoint.CurrentSubgoal;
        var latestGoalProgress =
            checkpoint.GoalProgress;

        if (resumableCheckpoint is not null)
        {
            taskHistory.Add(
                "CHECKPOINT-RESUME: Có checkpoint bị gián đoạn. Chỉ coi các mốc đã xác minh là lịch sử; BẮT BUỘC quan sát lại trạng thái desktop hiện tại trước khi làm action mới. Không replay action cuối từ checkpoint.");

            foreach (var milestone in checkpoint.VerifiedMilestones)
            {
                taskHistory.Add(
                    $"MỐC-CHECKPOINT-ĐÃ-XÁC-MINH: {milestone}");
            }

            progress.Add(
                "checkpoint-resume",
                $"Đã nạp checkpoint {checkpoint.Id:D}: {checkpoint.VerifiedMilestones.Count} mốc đã xác minh. Hệ thống sẽ quan sát lại desktop trước khi tiếp tục.",
                "resume",
                checkpoint.GoalProgress);
        }
        var lowConfidenceCount = 0;
        var blockedReplanCount = 0;
        var keyboardRepairFailures = 0;
        var keyboardResetRequired = false;
        var keyboardSelectionReady = false;
        string? plannerDegradedScene = null;
        var plannerDegradedCount = 0;
        var plannerBackoffUntil = DateTimeOffset.MinValue;
        IReadOnlyList<DesktopSceneElement> previousScene = Array.Empty<DesktopSceneElement>();
        var temporalSceneContext = string.Empty;

        try
        {
            for (var index = 1; index <= MaximumSteps; index++)
            {
                linked.Token.ThrowIfCancellationRequested();

                var gateBeforeRenewal = control.GetStatus();
                if (!control.EnsureScopedAutomationLease(
                        ScopedLeaseActions,
                        ScopedLeaseSeconds,
                        ScopedLeaseRenewalThresholdSeconds))
                {
                    MarkCheckpointStatusSafely(
                        checkpoint,
                        ComputerOperatorCheckpointStatuses.Interrupted);
                    taskTelemetry.Complete(
                        success: false,
                        route: "control-stop");
                    return Finish(
                        false,
                        gateBeforeRenewal.PauseReason ==
                            ComputerControlPauseReasons.HotkeyUnavailable
                            ? "Computer Operator đã dừng vì phím dừng khẩn cấp không còn khả dụng."
                            : "Computer Operator đã dừng theo yêu cầu người dùng.");
                }

                var gateAfterRenewal = control.GetStatus();
                if (gateBeforeRenewal.Paused &&
                    !gateAfterRenewal.Paused)
                {
                    progress.Add(
                        "safety-lease",
                        "Safety lease đã hết nhưng task vẫn còn hiệu lực; đã cấp lease mới trước khi tiếp tục. Đây không phải task timeout.",
                        "renewed");

                    progress.AddDiagnostic(
                        "lifecycle",
                        $"cycle={index}; event=safety-lease-renewed; taskContinues=true; remainingActions={gateAfterRenewal.RemainingActions}; expiresAt={gateAfterRenewal.ExpiresAt:O}.");
                }

                await execution.WaitIfPausedAsync(linked.Token);

                var stateSnapshot = index == 1
                    ? actionState.StartObservation(
                        "Bắt đầu quan sát cho bước đầu tiên.")
                    : actionState.ResetForNextStep(
                        "Bắt đầu quan sát cho bước tiếp theo.");

                progress.Add(
                    "action-state",
                    $"State machine: {stateSnapshot.State} — {stateSnapshot.Detail}",
                    stateSnapshot.State.ToString().ToLowerInvariant());

                var activeBeforeObservation = computer.GetActiveWindow();

                progress.Add(
                    "stabilize",
                    activeBeforeObservation is null
                        ? "Đang chờ desktop ổn định trước khi quan sát. Foreground chưa xác định."
                        : $"Đang chờ desktop ổn định trước khi quan sát. Foreground: {activeBeforeObservation.Title}.");

                DesktopScreenshotFrame frame;
                using var observeTelemetry =
                    telemetry.Begin(
                        ComputerOperatorTelemetryStages.Observe);

                try
                {
                    frame = await screenshots.CaptureStableVirtualScreenAsync(
                        maximumWaitMs: 5000,
                        linked.Token);

                    observeTelemetry.Complete(
                        success: true,
                        route: "desktop");

                    progress.Add(
                        "observe",
                        $"Đã chụp frame ổn định {frame.Width}x{frame.Height} lúc {frame.CapturedAtUtc:O}.",
                        observation: true);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    observeTelemetry.Complete(
                        success: false,
                        route: "error");

                    progress.Block(
                        $"Không chụp được desktop: {exception.Message}");
                    return Finish(
                        false,
                        $"Không chụp được desktop: {exception.Message}");
                }

                var desktopState = BuildDesktopState(frame);
                var sceneFingerprint =
                    BuildDesktopSceneFingerprint(desktopState);
                var windowsContext = desktopState.ToPromptSummary();
                var active = desktopState.ForegroundWindow;

                progress.Add(
                    "desktop-state",
                    $"Unified Desktop State: foreground={active?.Title ?? "không xác định"}; windows={desktopState.Windows.Count}; frame={desktopState.CaptureScope}/{desktopState.FrameWidth}x{desktopState.FrameHeight}.",
                    observation: true);

                var sceneDiagnosticId =
                    BuildDiagnosticId(sceneFingerprint);

                progress.AddDiagnostic(
                    "cycle",
                    $"cycle={index}; scene={sceneDiagnosticId}; foreground={active?.ProcessName ?? "?"}/{active?.Title ?? "không xác định"}; windows={desktopState.Windows.Count}; frame={desktopState.CaptureScope}; origin=({desktopState.FrameLeft},{desktopState.FrameTop}); size={desktopState.FrameWidth}x{desktopState.FrameHeight}.");

                DesktopOperatorDecision decision;
                var plannerStopwatch = Stopwatch.StartNew();
                using var planTelemetry =
                    telemetry.Begin(
                        ComputerOperatorTelemetryStages.GeminiPlan);

                var sameDegradedScene =
                    plannerDegradedScene is not null &&
                    plannerDegradedScene.Equals(
                        sceneFingerprint,
                        StringComparison.Ordinal);

                if (!sameDegradedScene)
                {
                    plannerDegradedCount = 0;
                    plannerBackoffUntil = DateTimeOffset.MinValue;
                }

                var providerBackoffActive =
                    sameDegradedScene &&
                    DateTimeOffset.UtcNow < plannerBackoffUntil;

                try
                {
                    if (providerBackoffActive)
                    {
                        var localHistory =
                            string.Join(
                                "\n",
                                taskHistory.TakeLast(40));

                        if (LocalPlanner.TryPlan(
                                normalizedGoal,
                                desktopState,
                                localHistory,
                                out decision))
                        {
                            plannerStopwatch.Stop();
                            planTelemetry.Complete(
                                success: true,
                                route: "local-planner");

                            progress.Add(
                                "local-planner",
                                $"Provider đang cooldown trên scene không đổi; dùng Local Planner: {decision.Reason}",
                                decision.Action,
                                decision.Confidence);

                            progress.AddDiagnostic(
                                "provider",
                                $"provider=Gemini; purpose=plan; cycle={index}; circuit=open; scene={sceneDiagnosticId}; backoffUntil={plannerBackoffUntil:O}; fallback=local-planner; action={decision.Action}.");
                        }
                        else
                        {
                            decision = CreatePlannerCooldownWaitDecision(
                                plannerBackoffUntil);

                            plannerStopwatch.Stop();
                            planTelemetry.Complete(
                                success: true,
                                route: "provider-cooldown");

                            progress.Add(
                                "provider-cooldown",
                                $"Scene chưa đổi và Gemini đang cooldown đến {plannerBackoffUntil:HH:mm:ss}; không gọi lặp provider.",
                                "wait");

                            progress.AddDiagnostic(
                                "provider",
                                $"provider=Gemini; purpose=plan; cycle={index}; circuit=open; scene={sceneDiagnosticId}; backoffUntil={plannerBackoffUntil:O}; fallback=safe-wait.");
                        }
                    }
                    else
                    {
                        progress.Add(
                            "analyze",
                            $"Đang gửi ảnh desktop {frame.Width}x{frame.Height} cho Vision để phân tích.");

                        decision = await vision.DecideComputerOperatorActionAsync(
                            frame,
                            normalizedGoal,
                            windowsContext,
                            BuildHistoryContext(
                                taskHistory,
                                recovery,
                                verifiedMilestones,
                                currentSubgoal,
                                latestGoalProgress),
                            temporalSceneContext,
                            linked.Token);

                        plannerStopwatch.Stop();
                        planTelemetry.Complete(
                            success: true,
                            route: "gemini");

                        if (IsProviderDegradedPlannerDecision(
                                decision))
                        {
                            plannerDegradedScene =
                                sceneFingerprint;
                            plannerDegradedCount++;

                            var backoffSeconds =
                                Math.Min(
                                    20,
                                    5 * plannerDegradedCount);

                            plannerBackoffUntil =
                                DateTimeOffset.UtcNow.AddSeconds(
                                    backoffSeconds);

                            progress.AddDiagnostic(
                                "provider",
                                $"provider=Gemini; purpose=plan; cycle={index}; latencyMs={plannerStopwatch.ElapsedMilliseconds}; result=degraded; scene={sceneDiagnosticId}; circuit=open; backoffSeconds={backoffSeconds}; action={decision.Action}; replaySideEffect=false.");
                        }
                        else
                        {
                            plannerDegradedScene = null;
                            plannerDegradedCount = 0;
                            plannerBackoffUntil = DateTimeOffset.MinValue;

                            progress.AddDiagnostic(
                                "provider",
                                $"provider=Gemini; purpose=plan; cycle={index}; latencyMs={plannerStopwatch.ElapsedMilliseconds}; action={decision.Action}; confidence={decision.Confidence:0.000}; payload=parsed.");
                        }
                    }
                }
                catch
                {
                    planTelemetry.Complete(
                        success: false,
                        route: "gemini");
                    throw;
                }
                finally
                {
                    frame.Clear();
                }

                var planState = actionState.MoveTo(
                    ComputerOperatorActionState.Plan,
                    $"Đã nhận quyết định {decision.Action} từ planner.");

                progress.Add(
                    "action-state",
                    $"State machine: {planState.State} — {planState.Detail}",
                    planState.State.ToString().ToLowerInvariant());

                var currentScene =
                    decision.SceneElements ?? Array.Empty<DesktopSceneElement>();

                if (previousScene.Count > 0 || currentScene.Count > 0)
                {
                    var temporalAnalysis = temporalScenes.Analyze(
                        previousScene,
                        currentScene);
                    temporalSceneContext = temporalAnalysis.ToPromptSummary();

                    progress.Add(
                        "temporal-scene",
                        $"Đối chiếu cảnh theo thời gian: ổn định={temporalAnalysis.StableCount}; di chuyển={temporalAnalysis.MovedCount}; xuất hiện={temporalAnalysis.AppearedIds.Count}; biến mất={temporalAnalysis.DisappearedIds.Count}.",
                        observation: true);

                    taskHistory.Add(
                        $"CẢNH-THEO-THỜI-GIAN: {temporalSceneContext}");
                }

                previousScene = currentScene
                    .Select(item => item with
                    {
                        Relations = item.Relations.ToArray()
                    })
                    .ToArray();

                if (!string.IsNullOrWhiteSpace(decision.CurrentSubgoal) &&
                    !decision.CurrentSubgoal.Equals(
                        currentSubgoal,
                        StringComparison.OrdinalIgnoreCase))
                {
                    currentSubgoal = decision.CurrentSubgoal.Trim();
                    taskHistory.Add(
                        $"MỤC-TIÊU-CON: {currentSubgoal}");
                    progress.Add(
                        "subgoal",
                        $"Mục tiêu con: {currentSubgoal}",
                        "plan",
                        decision.Confidence);
                }

                latestGoalProgress = decision.GoalProgress;

                progress.Add(
                    "goal-progress",
                    $"Tiến độ mục tiêu: khoảng {latestGoalProgress * 100:0}%.",
                    "plan",
                    decision.Confidence);

                foreach (var milestone in decision.VerifiedMilestones)
                {
                    var normalizedMilestone = milestone.Trim();
                    if (normalizedMilestone.Length == 0 ||
                        !verifiedMilestones.Add(normalizedMilestone))
                        continue;

                    taskHistory.Add(
                        $"MỐC-ĐÃ-QUAN-SÁT: {normalizedMilestone}");
                    progress.Add(
                        "milestone",
                        $"MỐC ĐÃ XÁC MINH: {normalizedMilestone}",
                        "verified",
                        decision.Confidence);
                }

                checkpoint = SaveCheckpointSafely(
                    checkpoint,
                    verifiedMilestones,
                    currentSubgoal,
                    latestGoalProgress);

                var loopStrategy =
                    decision.Action is "complete" or "blocked" or "wait"
                        ? string.Empty
                        : BuildSemanticActionSignature(decision);

                var loopAssessment = loopGuard.Observe(
                    sceneFingerprint,
                    loopStrategy);

                progress.Add(
                    "state",
                    string.IsNullOrWhiteSpace(decision.State)
                        ? "AI đã cập nhật trạng thái màn hình."
                        : $"Trạng thái: {decision.State}");

                if (loopAssessment.Detected)
                {
                    taskHistory.Add(
                        $"CẢNH-BÁO-VÒNG-LẶP {loopAssessment.Kind}: {loopAssessment.Detail}");

                    progress.Add(
                        "loop-detected",
                        $"Phát hiện nguy cơ vòng lặp: {loopAssessment.Detail} Cảnh báo tích lũy: {loopAssessment.Occurrences}.",
                        "replan",
                        decision.Confidence);

                    if (loopAssessment.RequiresStrategyChange)
                    {
                        taskHistory.Add(
                            "CHỈ DẪN THOÁT VÒNG LẶP: BẮT BUỘC đổi chiến lược. Không lặp lại cùng action/target. Nếu không còn phương án an toàn hợp lý, trả blocked và giải thích.");

                        progress.Add(
                            "replan",
                            "Chiến lược hiện tại đã nằm trong vòng lặp; không thực hiện lại. AI phải quan sát trạng thái hiện tại và chọn chiến lược khác.",
                            "replan",
                            decision.Confidence);

                        _ = actionState.MoveTo(
                            ComputerOperatorActionState.Replan,
                            "Loop guard yêu cầu đổi chiến lược.");

                        await Task.Delay(250, linked.Token);
                        continue;
                    }
                }

                progress.Add(
                    "plan",
                    string.IsNullOrWhiteSpace(decision.Plan)
                        ? "AI đang chọn bước tiếp theo từ trạng thái hiện tại."
                        : $"Kế hoạch: {decision.Plan}",
                    decision.Action,
                    decision.Confidence);

                progress.Add(
                    "decide",
                    $"Quyết định: {decision.Reason}",
                    decision.Action,
                    decision.Confidence);

                if (decision.Action == "complete")
                {
                    var completionState = actionState.MoveTo(
                        ComputerOperatorActionState.Success,
                        "Planner xác nhận mục tiêu đã hoàn thành.");

                    progress.Add(
                        "action-state",
                        $"State machine: {completionState.State} — {completionState.Detail}",
                        "success");
                    taskHistory.Add(
                        $"BƯỚC {index}: COMPLETE — {decision.Reason}");
                    progress.Complete(
                        $"Vision xác nhận mục tiêu đã đạt: {decision.Reason}");
                    MarkCheckpointStatusSafely(
                        checkpoint,
                        ComputerOperatorCheckpointStatuses.Completed);
                    taskTelemetry.Complete(
                        success: true,
                        route: "verified");
                    return Finish(
                        true,
                        steps.Count == 0
                            ? "Vision xác nhận mục tiêu đã ở trạng thái hoàn thành."
                            : $"Vision xác nhận tác vụ hoàn thành sau {steps.Count} bước.");
                }

                if (decision.Action == "blocked")
                {
                    blockedReplanCount++;
                    taskHistory.Add(
                        $"STEP {index}: BLOCKED-CANDIDATE ({blockedReplanCount}/3) — {decision.Reason}");

                    if (blockedReplanCount < 3 && index < MaximumSteps)
                    {
                        taskHistory.Add(
                            "CHỈ DẪN THỬ LẠI KHI BỊ CHẶN: Không coi blocked lần đầu là kết luận cuối. Hãy quan sát lại desktop hiện tại, kiểm tra các cửa sổ/scene mới và thử một chiến lược an toàn khác. Chỉ blocked lần nữa nếu thực sự không còn phương án hợp lý.");

                        progress.Add(
                            "replan",
                            $"Vision chưa tìm thấy bước an toàn ({blockedReplanCount}/3): {decision.Reason}. Sẽ chụp lại màn hình và thử chiến lược khác trước khi kết luận bị chặn.",
                            "replan",
                            decision.Confidence);

                        _ = actionState.MoveTo(
                            ComputerOperatorActionState.Replan,
                            "Blocked tạm thời; cần quan sát và thử chiến lược khác.");

                        await Task.Delay(350, linked.Token);
                        continue;
                    }

                    taskHistory.Add(
                        $"STEP {index}: BLOCKED-FINAL — {decision.Reason}");
                    var blockedState = actionState.MoveTo(
                        ComputerOperatorActionState.Blocked,
                        decision.Reason);

                    progress.Add(
                        "action-state",
                        $"State machine: {blockedState.State} — {blockedState.Detail}",
                        "blocked");

                    progress.Block(
                        $"Vision đã quan sát/lập lại phương án nhiều lần nhưng vẫn không còn bước an toàn: {decision.Reason}");
                    MarkCheckpointStatusSafely(
                        checkpoint,
                        ComputerOperatorCheckpointStatuses.Blocked);
                    taskTelemetry.Complete(
                        success: false,
                        route: "blocked");
                    return Finish(
                        false,
                        $"Vision dừng an toàn sau nhiều lần thử lại: {decision.Reason}");
                }

                blockedReplanCount = 0;

                if (decision.Action == "wait")
                {
                    taskHistory.Add(
                        $"STEP {index}: WAIT — {decision.Reason}");
                    progress.Add(
                        "wait",
                        $"Vision yêu cầu chờ rồi quan sát lại: {decision.Reason}",
                        "wait",
                        decision.Confidence);

                    _ = actionState.MoveTo(
                        ComputerOperatorActionState.Replan,
                        "Chờ UI ổn định rồi quan sát lại.");

                    await Task.Delay(900, linked.Token);
                    continue;
                }

                DesktopTargetTrackingResult? trackingResult = null;

                var targetState = actionState.MoveTo(
                    ComputerOperatorActionState.Target,
                    $"Chuẩn bị target cho action {decision.Action}.");

                progress.Add(
                    "action-state",
                    $"State machine: {targetState.State} — {targetState.Detail}",
                    "target");

                if (IsClickAction(decision.Action))
                {
                    var plannedWindow = ResolveTrackingWindow(
                        decision,
                        active,
                        frame);
                    var currentWindow = plannedWindow is null
                        ? null
                        : computer.GetWindows(50).Windows.FirstOrDefault(
                            window => window.WindowId.Equals(
                                plannedWindow.WindowId,
                                StringComparison.OrdinalIgnoreCase));

                    var tracking = TargetTracker.Track(
                        decision,
                        frame,
                        plannedWindow,
                        currentWindow);
                    trackingResult = tracking;

                    progress.Add(
                        "target-tracking",
                        tracking.Reason,
                        tracking.Adjusted ? "remap" : "track",
                        tracking.Confidence);

                    if (!tracking.SafeToExecute)
                    {
                        taskHistory.Add(
                            $"TARGET-TRACKING-REPLAN: {tracking.Reason}");
                        progress.Add(
                            "replan",
                            $"Target không còn an toàn để click: {tracking.Reason} Sẽ quan sát lại thay vì dùng tọa độ cũ.",
                            "replan",
                            tracking.Confidence);

                        var replanState = actionState.MoveTo(
                            ComputerOperatorActionState.Replan,
                            tracking.Reason);

                        progress.Add(
                            "action-state",
                            $"State machine: {replanState.State} — {replanState.Detail}",
                            "replan");

                        await Task.Delay(200, linked.Token);
                        continue;
                    }

                    decision = tracking.Decision;
                }

                if (RequiresExpectedEffect(decision.Action) &&
                    string.IsNullOrWhiteSpace(decision.ExpectedEffect))
                {
                    taskHistory.Add(
                        $"STEP {index}: REJECTED {decision.Action} — thiếu EXPECTED EFFECT để xác minh.");

                    recovery.RecordFailure(
                        BuildSemanticActionSignature(decision),
                        decision.Action,
                        ComputerOperatorFailureKinds.MissingExpectedEffect,
                        sceneFingerprint,
                        "Thiếu kết quả mong đợi để xác minh hành động.",
                        decision.ExpectedEffect,
                        decision.Confidence);

                    progress.Add(
                        "recovery",
                        "AI chưa nêu kết quả mong đợi có thể kiểm tra cho hành động này; không thực hiện và sẽ lập lại phương án.",
                        decision.Action,
                        decision.Confidence);

                    _ = actionState.MoveTo(
                        ComputerOperatorActionState.Replan,
                        "Thiếu expected effect; không được execute.");

                    await Task.Delay(300, linked.Token);
                    continue;
                }

                var confidenceAssessment =
                    ConfidenceEngine.AssessBeforeExecution(
                        decision,
                        currentScene,
                        trackingResult);

                progress.Add(
                    "confidence",
                    $"Confidence Engine: {confidenceAssessment.Reason}",
                    confidenceAssessment.Decision.ToString().ToLowerInvariant(),
                    confidenceAssessment.OverallConfidence);

                if (confidenceAssessment.Decision !=
                    ComputerOperatorConfidenceDecision.Execute)
                {
                    lowConfidenceCount++;

                    taskHistory.Add(
                        $"CONFIDENCE-REPLAN: {confidenceAssessment.Reason}");

                    recovery.RecordFailure(
                        BuildSemanticActionSignature(decision),
                        decision.Action,
                        ComputerOperatorFailureKinds.LowConfidence,
                        sceneFingerprint,
                        confidenceAssessment.Reason,
                        decision.ExpectedEffect,
                        confidenceAssessment.OverallConfidence);

                    if (lowConfidenceCount >= 3)
                    {
                        _ = actionState.MoveTo(
                            ComputerOperatorActionState.Blocked,
                            "Confidence Engine không đủ chắc chắn sau 3 lần quan sát.");

                        progress.Block(
                            "Confidence Engine không đủ chắc chắn để thực thi sau 3 lần quan sát.");
                        return Finish(
                            false,
                            "Đã dừng an toàn vì scene/target/action vẫn không đủ chắc chắn.");
                    }

                    _ = actionState.MoveTo(
                        ComputerOperatorActionState.Replan,
                        confidenceAssessment.Decision ==
                            ComputerOperatorConfidenceDecision.GeminiFallback
                            ? "Confidence trung gian; yêu cầu Gemini quan sát lại với ngữ cảnh mới."
                            : "Confidence thấp; cần re-observe trước khi execute.");

                    await Task.Delay(
                        confidenceAssessment.Decision ==
                            ComputerOperatorConfidenceDecision.GeminiFallback
                            ? 350
                            : 650,
                        linked.Token);
                    continue;
                }

                lowConfidenceCount = 0;

                var actionSignature = BuildSemanticActionSignature(decision);
                var strategyDiagnosticId =
                    BuildDiagnosticId(actionSignature);
                var previousStrategyFailures =
                    recovery.Attempts.Count(item =>
                        item.ActionSignature.Equals(
                            actionSignature,
                            StringComparison.OrdinalIgnoreCase));

                progress.AddDiagnostic(
                    "strategy",
                    $"cycle={index}; scene={sceneDiagnosticId}; strategy={strategyDiagnosticId}; previousFailures={previousStrategyFailures}; action={decision.Action}; targetLabel={LimitDiagnostic(decision.TargetLabel, 100)}; elementId={LimitDiagnostic(decision.TargetElementId, 80)}; bbox=({decision.BoxLeft},{decision.BoxTop},{decision.BoxWidth},{decision.BoxHeight}); plannerPoint=({decision.ImageX},{decision.ImageY}); coordinateSpace={decision.CoordinateSpace}; expectedEffect={LimitDiagnostic(decision.ExpectedEffect, 180)}; semantic={LimitDiagnostic(actionSignature, 220)}.",
                    decision.Action,
                    decision.Confidence);

                if (keyboardResetRequired)
                {
                    if (IsFineGrainedKeyboardRepair(decision))
                    {
                        taskHistory.Add(
                            "KEYBOARD-RESET-DIRECTIVE: Không được tiếp tục BACKSPACE/DELETE để vá từng ký tự. Hãy dùng Ctrl+A, xác minh vùng nhập đã được chọn, rồi type-text lại toàn bộ nội dung cuối cùng một lần.");

                        progress.Add(
                            "replan",
                            "Đã phát hiện vòng sửa chữ lặp. Không cho phép xóa từng ký tự nữa; AI phải chọn toàn bộ rồi nhập lại nội dung hoàn chỉnh.",
                            "keyboard-reset",
                            decision.Confidence);

                        _ = actionState.MoveTo(
                            ComputerOperatorActionState.Replan,
                            "Keyboard Reset Mode yêu cầu đổi chiến lược sửa text.");

                        await Task.Delay(220, linked.Token);
                        continue;
                    }

                    if (decision.Action == "type-text" &&
                        !keyboardSelectionReady)
                    {
                        taskHistory.Add(
                            "KEYBOARD-RESET-DIRECTIVE: Trước type-text phải press-hotkey Ctrl+A và xác minh selection. Không được gõ bù vào text đang sai.");

                        progress.Add(
                            "replan",
                            "Keyboard Reset Mode: chưa xác minh Ctrl+A; từ chối gõ bù để tránh lặp ký tự.",
                            "keyboard-reset",
                            decision.Confidence);

                        _ = actionState.MoveTo(
                            ComputerOperatorActionState.Replan,
                            "Chưa có selection an toàn trước khi nhập lại text.");

                        await Task.Delay(220, linked.Token);
                        continue;
                    }
                }

                if (recovery.ShouldAvoidRepeatedStrategy(
                        actionSignature,
                        sceneFingerprint,
                        out var avoidReason))
                {
                    taskHistory.Add(
                        $"STEP {index}: RECOVERY-SKIP {actionSignature} — {avoidReason}");

                    progress.Add(
                        "replan",
                        $"Không lặp lại chiến lược vừa thất bại: {avoidReason}",
                        decision.Action,
                        decision.Confidence);

                    progress.AddDiagnostic(
                        "recovery",
                        $"cycle={index}; scene={sceneDiagnosticId}; strategy={strategyDiagnosticId}; decision=REJECT-BEFORE-EXECUTE; reason={LimitDiagnostic(avoidReason, 260)}.",
                        decision.Action,
                        decision.Confidence);

                    _ = actionState.MoveTo(
                        ComputerOperatorActionState.Replan,
                        "Recovery memory yêu cầu chiến lược khác.");

                    await Task.Delay(250, linked.Token);
                    continue;
                }

                progress.Add(
                    "act",
                    $"Chuẩn bị thực hiện: {decision.Action}. {decision.Reason}",
                    decision.Action,
                    decision.Confidence);

                DesktopScreenshotFrame? verificationBaseline = null;
                VerificationCaptureContext? verificationCaptureContext = null;
                DesktopFastObserverSample? fastObserverBaseline = null;
                TextInteractionResult? textInteractionResult = null;

                if (!decision.Action.Equals(
                        "type-text",
                        StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        verificationBaseline = await CapturePostActionFrameAsync(linked.Token);
                        verificationCaptureContext =
                            VerificationCaptureContext.From(
                                verificationBaseline);
                        fastObserverBaseline = fastObserver.CaptureSample(verificationBaseline);
                        progress.Add(
                            "frame-baseline",
                            $"Đã khóa Verification Capture Context: scope={verificationCaptureContext.CaptureScope}; window={verificationCaptureContext.WindowId ?? "-"}; monitor={verificationCaptureContext.MonitorDevice ?? "-"}; origin=({verificationCaptureContext.Left},{verificationCaptureContext.Top}); size={verificationCaptureContext.Width}x{verificationCaptureContext.Height}; dpi={verificationCaptureContext.DpiX}x{verificationCaptureContext.DpiY}.",
                            observation: true);

                        progress.AddDiagnostic(
                            "capture",
                            $"cycle={index}; phase=before-action; scene={sceneDiagnosticId}; strategy={strategyDiagnosticId}; scope={verificationCaptureContext.CaptureScope}; window={verificationCaptureContext.WindowId ?? "-"}; monitor={verificationCaptureContext.MonitorDevice ?? "-"}; origin=({verificationCaptureContext.Left},{verificationCaptureContext.Top}); size={verificationCaptureContext.Width}x{verificationCaptureContext.Height}; dpi={verificationCaptureContext.DpiX}x{verificationCaptureContext.DpiY}.");
                    }
                    catch (Exception exception) when (
                        exception is ToolExecutionInputException or
                        InvalidOperationException)
                    {
                        logger.LogDebug(
                            exception,
                            "Không chụp được baseline frame difference; tiếp tục xác minh bằng ảnh hậu hành động.");
                    }
                }
                else
                {
                    progress.Add(
                        "text-engine",
                        "Type-text dùng Generic Text Interaction Engine; bỏ baseline Vision để ưu tiên local write/readback.",
                        "local");
                }

                if (!control.EnsureScopedAutomationLease(
                        ScopedLeaseActions,
                        ScopedLeaseSeconds,
                        ScopedLeaseRenewalThresholdSeconds))
                {
                    verificationBaseline?.Clear();
                    MarkCheckpointStatusSafely(
                        checkpoint,
                        ComputerOperatorCheckpointStatuses.Interrupted);
                    throw new ToolExecutionStoppedByUserException(
                        "Safety gate đã bị người dùng khóa hoặc phím dừng không còn khả dụng trước khi execute.");
                }

                ComputerActionResponse action;
                using var executionTelemetry =
                    telemetry.Begin(
                        decision.Action.Equals(
                            "type-text",
                            StringComparison.OrdinalIgnoreCase)
                            ? ComputerOperatorTelemetryStages.TextEngine
                            : ComputerOperatorTelemetryStages.Execute,
                        decision.Action);

                try
                {
                    var executeState = actionState.MoveTo(
                        ComputerOperatorActionState.Execute,
                        $"Thực thi action {decision.Action}.");

                    progress.Add(
                        "action-state",
                        $"State machine: {executeState.State} — {executeState.Detail}",
                        "execute");

                    await execution.WaitIfPausedAsync(linked.Token);

                    if (decision.Action.Equals(
                            "type-text",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        var activeForText = computer.GetActiveWindow()
                            ?? throw new ToolExecutionInputException(
                                "Không xác định được foreground window cho Text Engine.");

                        textInteractionResult = textInteraction.Execute(
                            new TextInteractionRequest(
                                activeForText.WindowId,
                                RequireValue(decision.Text, "text"),
                                TextInteractionWriteModes.ReplaceAll));

                        action = new ComputerActionResponse(
                            ComputerUseCapabilities.TypeText,
                            textInteractionResult.Applied || textInteractionResult.Verified,
                            textInteractionResult.Detail);

                        progress.Add(
                            "text-engine",
                            $"Text Engine: strategy={textInteractionResult.Strategy}; verified={textInteractionResult.Verified}; repair={textInteractionResult.RepairAttempted}; rollback={textInteractionResult.RolledBack}.",
                            textInteractionResult.Verified ? "verified" : "fallback",
                            textInteractionResult.Verified ? 1.0 : null);
                    }
                    else
                    {
                        action = actionExecutor.Execute(
                            decision,
                            frame);
                    }

                    executionTelemetry.Complete(
                        success: action.Applied,
                        route:
                            decision.Action.Equals(
                                "type-text",
                                StringComparison.OrdinalIgnoreCase)
                                ? "text-engine"
                                : "computer");
                }
                catch (ToolExecutionInputException exception)
                {
                    executionTelemetry.Complete(
                        success: false,
                        route: "rejected");

                    verificationBaseline?.Clear();
                    logger.LogWarning(
                        "Computer Operator step {Step} rejected: {Reason}",
                        index,
                        exception.Message);

                    var failurePolicy =
                        reliableOperator.ClassifyFailure(
                            exception.Message,
                            sideEffectMayHaveOccurred: false);

                    progress.Add(
                        "failure-policy",
                        $"Universal failure policy: {failurePolicy.Action} / {failurePolicy.Category} — {failurePolicy.Reason}",
                        failurePolicy.Action,
                        decision.Confidence);

                    taskHistory.Add(
                        $"FAILURE-POLICY: {failurePolicy.Action}; {failurePolicy.Category}; {failurePolicy.Reason}");

                    if (failurePolicy.Action ==
                        UniversalFailureActions.Stop)
                    {
                        MarkCheckpointStatusSafely(
                            checkpoint,
                            ComputerOperatorCheckpointStatuses.Blocked);

                        progress.Block(
                            $"Dừng theo failure policy: {failurePolicy.Reason}");

                        return Finish(
                            false,
                            $"Computer Operator dừng an toàn: {failurePolicy.Reason}");
                    }

                    if (failurePolicy.Action ==
                        UniversalFailureActions.Wait)
                    {
                        _ = actionState.MoveTo(
                            ComputerOperatorActionState.Replan,
                            "Failure policy yêu cầu chờ và quan sát lại.");

                        await Task.Delay(
                            750,
                            linked.Token);
                        continue;
                    }

                    var failures = recovery.RecordFailure(
                        actionSignature,
                        decision.Action,
                        ComputerOperatorFailureKinds.ActionRejected,
                        sceneFingerprint,
                        exception.Message,
                        decision.ExpectedEffect,
                        decision.Confidence);

                    taskHistory.Add(
                        $"STEP {index}: FAILED {actionSignature} — {exception.Message}");

                    RecordOutcomeLoop(
                        loopGuard,
                        taskHistory,
                        decision,
                        sceneFingerprint,
                        actionSignature,
                        exception.Message);

                    var diagnoseState = actionState.MoveTo(
                        ComputerOperatorActionState.Diagnose,
                        exception.Message);
                    var replanState = actionState.MoveTo(
                        ComputerOperatorActionState.Replan,
                        "Hành động bị từ chối; cần lập phương án khác.");

                    progress.Add(
                        "action-state",
                        $"State machine: {diagnoseState.State} -> {replanState.State}",
                        "replan");

                    var recoveryPlan = RecoveryEngine.Plan(
                        new ComputerOperatorFailureContext(
                            ClassifyFailureKind(
                                decision.Action,
                                exception.Message,
                                actionApplied: false,
                                verificationFailed: false),
                            decision.Action,
                            exception.Message,
                            RepeatedFailures: failures));

                    progress.Add(
                        "recovery-plan",
                        $"Recovery Engine: {recoveryPlan.PrimaryAction}; fallback={string.Join(",", recoveryPlan.Fallbacks)}; retrySame={recoveryPlan.AllowSameStrategyRetry}. {recoveryPlan.Reason}",
                        recoveryPlan.PrimaryAction.ToString().ToLowerInvariant(),
                        decision.Confidence);

                    taskHistory.Add(
                        $"RECOVERY-PLAN: {recoveryPlan.PrimaryAction}; {recoveryPlan.Reason}");

                    progress.Add(
                        "recovery",
                        $"Hành động bị từ chối ({failures} lần với chiến lược này): {exception.Message}. AI sẽ quan sát lại và lập phương án khác.",
                        decision.Action,
                        decision.Confidence);

                    if (failures >= 3)
                    {
                        taskHistory.Add(
                            "CHỈ DẪN LẬP LẠI PHƯƠNG ÁN: chiến lược này đã bị loại bỏ. Phải chọn chiến lược khác; chỉ trả blocked nếu không còn lựa chọn an toàn hợp lý.");
                        progress.Add(
                            "replan",
                            "Chiến lược bị từ chối nhiều lần và đã được đánh dấu không dùng lại. AI phải đổi cách tiếp cận.",
                            "replan",
                            decision.Confidence);
                    }

                    await Task.Delay(500, linked.Token);
                    continue;
                }

                steps.Add(new ComputerOperatorTaskStep(
                    index,
                    decision.Action,
                    action.Detail));

                if (!action.Applied)
                {
                    verificationBaseline?.Clear();
                    var failures = recovery.RecordFailure(
                        actionSignature,
                        decision.Action,
                        ComputerOperatorFailureKinds.NotApplied,
                        sceneFingerprint,
                        action.Detail,
                        decision.ExpectedEffect,
                        decision.Confidence);

                    taskHistory.Add(
                        $"STEP {index}: NOT-APPLIED {actionSignature} — {action.Detail}");

                    RecordOutcomeLoop(
                        loopGuard,
                        taskHistory,
                        decision,
                        sceneFingerprint,
                        actionSignature,
                        action.Detail);

                    var diagnoseState = actionState.MoveTo(
                        ComputerOperatorActionState.Diagnose,
                        action.Detail);
                    var replanState = actionState.MoveTo(
                        ComputerOperatorActionState.Replan,
                        "Action không được áp dụng; cần re-plan.");

                    progress.Add(
                        "action-state",
                        $"State machine: {diagnoseState.State} -> {replanState.State}",
                        "replan");

                    var recoveryPlan = RecoveryEngine.Plan(
                        new ComputerOperatorFailureContext(
                            ClassifyFailureKind(
                                decision.Action,
                                action.Detail,
                                actionApplied: false,
                                verificationFailed: false),
                            decision.Action,
                            action.Detail,
                            RepeatedFailures: failures));

                    progress.Add(
                        "recovery-plan",
                        $"Recovery Engine: {recoveryPlan.PrimaryAction}; fallback={string.Join(",", recoveryPlan.Fallbacks)}; retrySame={recoveryPlan.AllowSameStrategyRetry}. {recoveryPlan.Reason}",
                        recoveryPlan.PrimaryAction.ToString().ToLowerInvariant(),
                        decision.Confidence);

                    taskHistory.Add(
                        $"RECOVERY-PLAN: {recoveryPlan.PrimaryAction}; {recoveryPlan.Reason}");

                    progress.Add(
                        "recovery",
                        $"Hành động chưa tạo thay đổi ({failures} lần với chiến lược này): {action.Detail}. Sẽ quan sát lại và lập phương án khác.",
                        decision.Action,
                        decision.Confidence);

                    if (failures >= 3)
                    {
                        taskHistory.Add(
                            "REPLAN-DIRECTIVE: chiến lược không hiệu lực đã bị loại bỏ. Phải chọn chiến lược khác; chỉ trả blocked nếu không còn lựa chọn an toàn hợp lý.");
                        progress.Add(
                            "replan",
                            "Chiến lược không tạo thay đổi nhiều lần và đã bị loại bỏ. AI phải đổi cách tiếp cận.",
                            "replan",
                            decision.Confidence);
                    }

                    await Task.Delay(500, linked.Token);
                    continue;
                }

                progress.Add(
                    "acted",
                    action.Detail,
                    decision.Action,
                    decision.Confidence,
                    actionTaken: true);

                var verifyState = actionState.MoveTo(
                    ComputerOperatorActionState.Verify,
                    $"Xác minh kết quả của action {decision.Action}.");

                progress.Add(
                    "action-state",
                    $"State machine: {verifyState.State} — {verifyState.Detail}",
                    "verify");

                ActionVerificationResult verification;
                var authoritativeLocalText =
                    textInteractionResult is
                    {
                        Verified: true,
                        ActualText: not null
                    };

                if (authoritativeLocalText)
                {
                    verificationBaseline?.Clear();
                    verification = new(
                        true,
                        1.0,
                        $"Local Text Verifier: {textInteractionResult!.Detail}");

                    progress.Add(
                        "verification-route",
                        "Text verification hoàn tất cục bộ; không gọi screenshot/Gemini.",
                        "local",
                        1.0);

                    progress.Add(
                        "confidence-verify",
                        "Local exact text readback là bằng chứng xác định; bỏ composite Vision confidence.",
                        "verified",
                        1.0);
                }
                else
                {
                    using var verificationTelemetry =
                        telemetry.Begin(
                            ComputerOperatorTelemetryStages.Verify,
                            decision.Action);

                    try
                    {
                        verification = await VerifyAppliedActionAsync(
                            decision,
                            frame,
                            verificationBaseline,
                            verificationCaptureContext,
                            fastObserverBaseline,
                            linked.Token);

                        verificationTelemetry.Complete(
                            verification.Verified,
                            verification.Detail.StartsWith(
                                "Gemini Vision",
                                StringComparison.OrdinalIgnoreCase)
                                ? "semantic"
                                : "local");
                    }
                    catch
                    {
                        verificationTelemetry.Complete(
                            success: false,
                            route: "error");
                        throw;
                    }
                    finally
                    {
                        verificationBaseline?.Clear();
                    }

                    var verificationAssessment =
                        ConfidenceEngine.AssessVerification(
                            decision,
                            observation: null,
                            verification.Confidence,
                            verification.Detail.StartsWith(
                                "Gemini Vision",
                                StringComparison.OrdinalIgnoreCase));

                    progress.Add(
                        "confidence-verify",
                        $"Verification confidence: {verificationAssessment.Reason}",
                        verificationAssessment.Decision.ToString().ToLowerInvariant(),
                        verificationAssessment.OverallConfidence);

                    if (verification.Verified &&
                        verificationAssessment.OverallConfidence < MinimumConfidence)
                    {
                        verification = verification with
                        {
                            Verified = false,
                            Confidence = verificationAssessment.OverallConfidence,
                            Detail =
                                $"Composite confidence chưa đủ để chấp nhận verification. {verificationAssessment.Reason}"
                        };
                    }
                }

                progress.AddDiagnostic(
                    "evidence",
                    $"cycle={index}; scene={sceneDiagnosticId}; strategy={strategyDiagnosticId}; verified={verification.Verified}; inconclusive={verification.Inconclusive}; confidence={verification.Confidence:0.000}; detail={LimitDiagnostic(verification.Detail, 300)}.",
                    decision.Action,
                    verification.Confidence);

                if (!verification.Verified &&
                    verification.Inconclusive)
                {
                    taskHistory.Add(
                        $"STEP {index}: VERIFY-INCONCLUSIVE {actionSignature} — {verification.Detail}");

                    taskHistory.Add(
                        "CHỈ DẪN: Semantic verifier tạm thời không khả dụng. Không được replay side effect vừa thực hiện. Phải quan sát lại desktop hiện tại, ưu tiên structured/local evidence và chỉ gọi semantic verifier lại khi cần.");

                    progress.Add(
                        "replan",
                        $"Xác minh chưa thể kết luận: {verification.Detail}",
                        "inconclusive",
                        verification.Confidence);

                    var diagnoseState = actionState.MoveTo(
                        ComputerOperatorActionState.Diagnose,
                        "Verification chưa thể kết luận; không replay side effect.");

                    var replanState = actionState.MoveTo(
                        ComputerOperatorActionState.Replan,
                        "Verification inconclusive; quan sát lại trước action mới.");

                    progress.Add(
                        "action-state",
                        $"State machine: {diagnoseState.State} -> {replanState.State} — không replay action đã thực hiện.",
                        "replan");

                    await Task.Delay(
                        350,
                        linked.Token);
                    continue;
                }

                if (!verification.Verified)
                {
                    if (IsKeyboardAction(decision.Action))
                    {
                        keyboardRepairFailures++;

                        if (keyboardRepairFailures >= 2)
                        {
                            keyboardResetRequired = true;
                            keyboardSelectionReady = false;

                            taskHistory.Add(
                                "KEYBOARD-RESET-DIRECTIVE: Đã có ít nhất 2 lần sửa text không đạt. Không sửa từng ký tự nữa. Bắt buộc Ctrl+A -> verify selection -> type-text toàn bộ nội dung đích -> verify.");

                            progress.Add(
                                "recovery",
                                "Phát hiện vòng sửa bàn phím lặp. Chuyển sang Keyboard Reset Mode: chọn toàn bộ và nhập lại nội dung hoàn chỉnh.",
                                "keyboard-reset",
                                verification.Confidence);
                        }
                    }

                    var failures = recovery.RecordFailure(
                        actionSignature,
                        decision.Action,
                        ComputerOperatorFailureKinds.VerificationFailed,
                        sceneFingerprint,
                        verification.Detail,
                        decision.ExpectedEffect,
                        verification.Confidence);

                    taskHistory.Add(
                        $"STEP {index}: VERIFY-FAILED {actionSignature} — {verification.Detail}; EXPECTED: {decision.ExpectedEffect}");

                    RecordOutcomeLoop(
                        loopGuard,
                        taskHistory,
                        decision,
                        sceneFingerprint,
                        actionSignature,
                        verification.Detail);

                    var diagnoseState = actionState.MoveTo(
                        ComputerOperatorActionState.Diagnose,
                        verification.Detail);
                    var replanState = actionState.MoveTo(
                        ComputerOperatorActionState.Replan,
                        "Verification thất bại; cần re-plan.");

                    progress.Add(
                        "action-state",
                        $"State machine: {diagnoseState.State} -> {replanState.State}",
                        "replan");

                    var recoveryPlan = RecoveryEngine.Plan(
                        new ComputerOperatorFailureContext(
                            ClassifyFailureKind(
                                decision.Action,
                                verification.Detail,
                                actionApplied: true,
                                verificationFailed: true),
                            decision.Action,
                            verification.Detail,
                            RepeatedFailures: failures));

                    progress.Add(
                        "recovery-plan",
                        $"Recovery Engine: {recoveryPlan.PrimaryAction}; fallback={string.Join(",", recoveryPlan.Fallbacks)}; retrySame={recoveryPlan.AllowSameStrategyRetry}. {recoveryPlan.Reason}",
                        recoveryPlan.PrimaryAction.ToString().ToLowerInvariant(),
                        verification.Confidence);

                    taskHistory.Add(
                        $"RECOVERY-PLAN: {recoveryPlan.PrimaryAction}; {recoveryPlan.Reason}");

                    progress.AddDiagnostic(
                        "recovery",
                        $"cycle={index}; scene={sceneDiagnosticId}; strategy={strategyDiagnosticId}; failureKind={ClassifyFailureKind(decision.Action, verification.Detail, actionApplied: true, verificationFailed: true)}; failures={failures}; retrySame={recoveryPlan.AllowSameStrategyRetry}; primary={recoveryPlan.PrimaryAction}; fallbacks={string.Join(",", recoveryPlan.Fallbacks)}; reason={LimitDiagnostic(recoveryPlan.Reason, 240)}.",
                        decision.Action,
                        verification.Confidence);

                    progress.Add(
                        "recovery",
                        $"Kết quả thực tế không khớp mong đợi ({failures} lần với chiến lược này): {verification.Detail}. AI sẽ quan sát lại và đổi phương án.",
                        decision.Action,
                        verification.Confidence);

                    if (failures >= 3)
                    {
                        taskHistory.Add(
                            "REPLAN-DIRECTIVE: chiến lược không đạt expected effect đã bị loại bỏ. Phải chọn chiến lược khác; chỉ trả blocked nếu không còn lựa chọn an toàn hợp lý.");
                        progress.Add(
                            "replan",
                            "Chiến lược không đạt kết quả mong đợi nhiều lần và đã bị loại bỏ. AI phải đổi cách tiếp cận.",
                            "replan",
                            verification.Confidence);
                    }

                    await Task.Delay(350, linked.Token);
                    continue;
                }

                if (keyboardResetRequired &&
                    IsSelectAllHotkey(decision))
                {
                    keyboardSelectionReady = true;
                    taskHistory.Add(
                        "KEYBOARD-RESET: Ctrl+A đã được xác minh; bước tiếp theo phải type-text toàn bộ nội dung cuối cùng.");
                    progress.Add(
                        "milestone",
                        "Đã chọn toàn bộ vùng nhập; sẵn sàng nhập lại nội dung hoàn chỉnh.",
                        "keyboard-reset",
                        verification.Confidence);
                }
                else if (keyboardResetRequired &&
                         decision.Action == "type-text" &&
                         keyboardSelectionReady)
                {
                    keyboardRepairFailures = 0;
                    keyboardResetRequired = false;
                    keyboardSelectionReady = false;
                    taskHistory.Add(
                        "KEYBOARD-RESET: type-text toàn bộ đã được xác minh; thoát Keyboard Reset Mode.");
                    progress.Add(
                        "milestone",
                        "Nội dung nhập lại đã được xác minh; kết thúc chế độ sửa chữ.",
                        "keyboard-reset",
                        verification.Confidence);
                }
                else if (IsKeyboardAction(decision.Action) &&
                         !keyboardResetRequired)
                {
                    keyboardRepairFailures = 0;
                }

                taskHistory.Add(
                    $"STEP {index}: VERIFIED {actionSignature} — {verification.Detail}; EXPECTED: {decision.ExpectedEffect}");

                checkpoint = SaveCheckpointSafely(
                    checkpoint,
                    verifiedMilestones,
                    currentSubgoal,
                    latestGoalProgress,
                    decision.Action,
                    decision.ExpectedEffect);

                if (!string.IsNullOrWhiteSpace(decision.ExpectedEffect) &&
                    verifiedMilestones.Add(decision.ExpectedEffect.Trim()))
                {
                    taskHistory.Add(
                        $"MỐC-HỆ-THỐNG-ĐÃ-XÁC-MINH: {decision.ExpectedEffect.Trim()}");
                    progress.Add(
                        "milestone",
                        $"Mốc hệ thống đã xác minh: {decision.ExpectedEffect.Trim()}",
                        "verified",
                        verification.Confidence);
                }

                var actionSuccessState = actionState.MoveTo(
                    ComputerOperatorActionState.Success,
                    "Action đã được xác minh thành công.");

                progress.Add(
                    "action-state",
                    $"State machine: {actionSuccessState.State} — {actionSuccessState.Detail}",
                    "success");

                loopGuard.MarkProgress();

                progress.Add(
                    "verify-result",
                    verification.Detail,
                    "verified",
                    verification.Confidence);
            }

            progress.Block(
                $"Đạt giới hạn {MaximumSteps} bước để tránh vòng lặp.");
            MarkCheckpointStatusSafely(
                checkpoint,
                ComputerOperatorCheckpointStatuses.Interrupted);
            taskTelemetry.Complete(
                success: false,
                route: "step-limit");
            return Finish(
                false,
                $"Đã đạt giới hạn {MaximumSteps} bước nên dừng để tránh vòng lặp.");
        }
        catch (OperationCanceledException) when (operatorToken.IsCancellationRequested)
        {
            taskTelemetry.Complete(
                success: false,
                route: "cancelled");
            MarkCheckpointStatusSafely(
                checkpoint,
                ComputerOperatorCheckpointStatuses.Interrupted);
            progress.StopByUser();
            throw new ToolExecutionStoppedByUserException(
                "Người dùng đã dừng Computer Operator từ AI Operator Console.");
        }
        catch (OperationCanceledException)
        {
            taskTelemetry.Complete(
                success: false,
                route: "cancelled");
            MarkCheckpointStatusSafely(
                checkpoint,
                ComputerOperatorCheckpointStatuses.Interrupted);
            progress.Block("Computer Operator đã bị hủy.");
            throw;
        }
        catch (Exception exception)
        {
            taskTelemetry.Complete(
                success: false,
                route: "error");
            MarkCheckpointStatusSafely(
                checkpoint,
                ComputerOperatorCheckpointStatuses.Interrupted);
            progress.Block(
                $"Computer Operator gặp lỗi: {exception.Message}");
            throw;
        }
        finally
        {
            execution.Complete();
        }

        ComputerOperatorTaskResult Finish(
            bool completed,
            string summary) =>
            new(
                normalizedGoal,
                completed,
                summary,
                steps.ToArray(),
                "Gemini",
                vision.Model);
    }

    private sealed record ActionVerificationResult(
        bool Verified,
        double Confidence,
        string Detail,
        bool Inconclusive = false);

    private async Task<ActionVerificationResult> VerifyAppliedActionAsync(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame previousFrame,
        DesktopScreenshotFrame? verificationBaseline,
        VerificationCaptureContext? verificationCaptureContext,
        DesktopFastObserverSample? fastObserverBaseline,
        CancellationToken cancellationToken)
    {
        progress.Add(
            "verify",
            decision.Action == "move-pointer"
                ? "Đang chụp trạng thái mới và kiểm tra vị trí con trỏ sau khi di chuyển."
                : $"Đang chụp trạng thái mới để xác minh: {decision.ExpectedEffect}",
            decision.Action,
            decision.Confidence);

        await Task.Delay(
            decision.Action == "move-pointer" ? 180 : 550,
            cancellationToken);
        await execution.WaitIfPausedAsync(cancellationToken);

        DesktopScreenshotFrame after =
            await CapturePostActionFrameAsync(
                verificationCaptureContext,
                cancellationToken);

        try
        {
            progress.Add(
                "observe",
                $"Đã chụp frame hậu hành động {after.Width}x{after.Height}; scope={after.CaptureScope}; window={after.WindowId ?? "-"}; foreground={after.WindowWasForeground}.",
                observation: true);

            if (decision.Action == "move-pointer")
            {
                var expected = coordinates.ToDesktopPoint(
                    BuildCoordinateRequest(decision, useEnd: false),
                    previousFrame);
                var actual = computer.GetCursorPosition();
                var deltaX = Math.Abs(actual.X - expected.DesktopX);
                var deltaY = Math.Abs(actual.Y - expected.DesktopY);
                var verified = deltaX <= 3 && deltaY <= 3;

                return new(
                    verified,
                    verified ? 1.0 : 0.0,
                    verified
                        ? $"Đã chụp lại màn hình; con trỏ ở ({actual.X},{actual.Y}), khớp điểm mong đợi ({expected.DesktopX},{expected.DesktopY})."
                        : $"Đã chụp lại màn hình; con trỏ ở ({actual.X},{actual.Y}), lệch khỏi điểm mong đợi ({expected.DesktopX},{expected.DesktopY}).");
            }

            var verificationContextChanged =
                verificationCaptureContext is not null &&
                !verificationCaptureContext.Matches(after);

            if (verificationContextChanged)
            {
                progress.Add(
                    "capture-context-changed",
                    $"Verification Capture Context đã thay đổi sau action: trước={verificationCaptureContext!.CaptureScope}/{verificationCaptureContext.Width}x{verificationCaptureContext.Height}/window={verificationCaptureContext.WindowId ?? "-"}; sau={after.CaptureScope}/{after.Width}x{after.Height}/window={after.WindowId ?? "-"}. Không so sánh pixel giữa hai context khác nhau; coi đây là transition evidence và tiếp tục structured/semantic verification.",
                    "transition",
                    observation: true);
            }

            var frameDifference =
                verificationBaseline is null ||
                verificationContextChanged
                    ? null
                    : frameDifferences.Compare(
                        verificationBaseline,
                        after);

            if (frameDifference is not null)
            {
                progress.Add(
                    "frame-difference",
                    frameDifference.Comparable
                        ? $"Frame difference: {frameDifference.ChangedRatio * 100:0.00}% mẫu thay đổi; vùng=({frameDifference.BoxLeft},{frameDifference.BoxTop},{frameDifference.BoxWidth},{frameDifference.BoxHeight}); meanDelta={frameDifference.MeanChannelDelta:0.0}."
                        : $"Frame difference không khả dụng: {frameDifference.Reason}",
                    observation: true);
            }

            DesktopVerificationRoutingResult? localRouteForFusion = null;

            if (fastObserverBaseline is not null)
            {
                var fastAfter = fastObserver.CaptureSample(after);
                var fastObservation = fastObserver.Analyze(
                    fastObserverBaseline,
                    fastAfter,
                    frameDifference);

                progress.Add(
                    "fast-observer",
                    $"Quan sát cục bộ nhanh: {fastObservation.Summary}",
                    observation: true);

                var route = VerificationRouter.Route(
                    decision,
                    fastObservation,
                    frameDifference);

                progress.Add(
                    "verification-route",
                    $"Verification Router: {route.Route} — {route.Reason}",
                    route.Route == DesktopVerificationRoute.GeminiRequired
                        ? "gemini"
                        : "local",
                    route.Confidence);

                if (route.Route == DesktopVerificationRoute.LocalVerified)
                {
                    localRouteForFusion = route;

                    var localDecision =
                        reliableOperator.EvaluateLocalVerification(
                            decision,
                            route);

                    progress.Add(
                        "evidence-fusion",
                        $"Evidence Fusion local: {localDecision.Source} — {localDecision.Reason}",
                        localDecision.Verified
                            ? "verified"
                            : "semantic-required",
                        localDecision.Confidence);

                    if (localDecision.Verified)
                    {
                        return new(
                            true,
                            localDecision.Confidence,
                            $"Universal Reliable Operator xác minh local: {localDecision.Reason}");
                    }

                    // Một strong-local source đơn lẻ (ví dụ scroll/frame)
                    // không được tự complete. Tiếp tục semantic verification.
                }

                var shouldAdaptiveWait =
                    ComputerOperatorAdaptiveWaitPolicy.SupportsAdaptiveWaiting(
                        decision.Action) &&
                    (route.Route == DesktopVerificationRoute.LocalFailed ||
                     (route.Route == DesktopVerificationRoute.GeminiRequired &&
                      !fastObservation.ForegroundWindowChanged &&
                      !fastObservation.WindowBoundsChanged &&
                      (frameDifference?.Comparable != true ||
                       frameDifference.ChangedRatio < 0.01)));

                if (shouldAdaptiveWait)
                {
                    var adaptiveResult =
                        await WaitForAdaptiveTransitionAsync(
                            decision,
                            verificationBaseline ?? previousFrame,
                            verificationCaptureContext,
                            fastObserverBaseline ??
                                fastObserver.CaptureSample(
                                    verificationBaseline ?? previousFrame),
                            cancellationToken);

                    if (adaptiveResult is not null)
                    {
                        return adaptiveResult;
                    }

                    // Sau thời gian chờ phải dùng observation mới nhất cho
                    // semantic verification, tuyệt đối không dùng frame cũ.
                    after.Clear();
                    after = await CapturePostActionFrameAsync(
                        verificationCaptureContext,
                        cancellationToken);

                    verificationContextChanged =
                        verificationCaptureContext is not null &&
                        !verificationCaptureContext.Matches(after);

                    frameDifference =
                        verificationContextChanged
                            ? null
                            : (verificationBaseline ?? previousFrame) is { } reference
                                ? frameDifferences.Compare(
                                    reference,
                                    after)
                                : null;

                    if (verificationContextChanged)
                    {
                        progress.Add(
                            "capture-context-changed",
                            "Context capture đã thay đổi trong lúc chờ thích ứng; bỏ pixel diff cũ và chuyển sang xác minh trạng thái mới.",
                            "transition",
                            observation: true);
                    }

                    progress.Add(
                        "adaptive-wait",
                        "Bộ chờ thích ứng chưa có bằng chứng cuối cùng; đã chụp trạng thái mới nhất để chuyển sang xác minh ngữ nghĩa.",
                        "semantic");
                }
                else if (route.Route == DesktopVerificationRoute.LocalFailed)
                {
                    return new(
                        false,
                        route.Confidence,
                        $"Xác minh cục bộ thất bại: {route.Reason}");
                }

                var adaptive = GeminiCallPolicy.EvaluateVerification(
                    decision,
                    fastObservation,
                    frameDifference);

                progress.Add(
                    "gemini-call-policy",
                    $"Adaptive Gemini: {adaptive.Decision} — {adaptive.Reason}",
                    adaptive.Decision == AdaptiveGeminiDecision.CallGemini
                        ? "gemini"
                        : "local",
                    adaptive.Confidence);

                if (adaptive.Decision == AdaptiveGeminiDecision.SkipAndPass)
                {
                    return new(
                        true,
                        adaptive.Confidence,
                        $"Không gọi Gemini: {adaptive.Reason}");
                }

                if (adaptive.Decision == AdaptiveGeminiDecision.SkipAndFail)
                {
                    return new(
                        false,
                        adaptive.Confidence,
                        $"Không gọi Gemini: {adaptive.Reason}");
                }
            }

            var visionFrame = RoiVision.SelectVerificationFrame(
                decision,
                previousFrame,
                after,
                frameDifference);

            try
            {
                progress.Add(
                    "roi-vision",
                    $"Gemini verification dùng {visionFrame.Source}: {visionFrame.Frame.Width}x{visionFrame.Frame.Height}; origin=({visionFrame.Frame.Left},{visionFrame.Frame.Top}).",
                    observation: true);

                using var geminiVerifyTelemetry =
                    telemetry.Begin(
                        ComputerOperatorTelemetryStages.GeminiVerify,
                        decision.Action);

                DesktopVisionVerification result;
                var verifierStopwatch = Stopwatch.StartNew();
                try
                {
                    result = await vision.VerifyAsync(
                        visionFrame.Frame,
                        decision.ExpectedEffect,
                        frameDifference,
                        cancellationToken);

                    verifierStopwatch.Stop();
                    geminiVerifyTelemetry.Complete(
                        result.Satisfied,
                        "gemini");

                    progress.AddDiagnostic(
                        "provider",
                        $"provider=Gemini; purpose=verify; latencyMs={verifierStopwatch.ElapsedMilliseconds}; satisfied={result.Satisfied}; confidence={result.Confidence:0.000}; route={visionFrame.Source}.");
                }
                catch (HttpRequestException exception)
                    when (IsTransientVisionFailure(exception))
                {
                    verifierStopwatch.Stop();
                    progress.AddDiagnostic(
                        "provider",
                        $"provider=Gemini; purpose=verify; latencyMs={verifierStopwatch.ElapsedMilliseconds}; result=transient-error; http={(int?)exception.StatusCode ?? 0}; replaySideEffect=false.");

                    progress.Add(
                        "vision-transient",
                        $"Gemini Vision tạm thời không khả dụng ({(int?)exception.StatusCode ?? 0}). Chờ ngắn rồi thử xác minh lại một lần; không replay action.",
                        "wait");

                    await Task.Delay(
                        900,
                        cancellationToken);

                    try
                    {
                        result = await vision.VerifyAsync(
                            visionFrame.Frame,
                            decision.ExpectedEffect,
                            frameDifference,
                            cancellationToken);

                        geminiVerifyTelemetry.Complete(
                            result.Satisfied,
                            "gemini-retry");
                    }
                    catch (HttpRequestException retryException)
                        when (IsTransientVisionFailure(retryException))
                    {
                        geminiVerifyTelemetry.Complete(
                            success: false,
                            route: "gemini-transient");

                        return new(
                            Verified: false,
                            Confidence: 0,
                            Detail:
                                $"Gemini Vision tạm thời không khả dụng sau lần thử lại (HTTP {(int?)retryException.StatusCode ?? 0}). Kết quả hành động chưa thể kết luận; phải quan sát lại trạng thái hiện tại và tuyệt đối không replay side effect.",
                            Inconclusive: true);
                    }
                }
                catch
                {
                    geminiVerifyTelemetry.Complete(
                        success: false,
                        route: "gemini");
                    throw;
                }

                var reliableVerification =
                    reliableOperator.EvaluateSemanticVerification(
                        decision,
                        localRouteForFusion,
                        result.Satisfied,
                        result.Confidence,
                        result.Reason);

                progress.Add(
                    "evidence-fusion",
                    $"Evidence Fusion semantic: {reliableVerification.Source} — {reliableVerification.Reason}",
                    reliableVerification.Verified
                        ? "verified"
                        : "not-verified",
                    reliableVerification.Confidence);

                return new(
                    reliableVerification.Verified,
                    reliableVerification.Confidence,
                    $"Universal Reliable Operator ({visionFrame.Source}): {reliableVerification.Reason}");
            }
            finally
            {
                visionFrame.Clear();
            }
        }
        finally
        {
            after.Clear();
        }
    }

    private static bool IsProviderDegradedPlannerDecision(
        DesktopOperatorDecision decision)
    {
        if (!decision.Action.Equals(
                "wait",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var reason =
            decision.Reason ?? string.Empty;

        return reason.Contains(
                   "Gemini planning tạm thời không khả dụng",
                   StringComparison.OrdinalIgnoreCase) ||
               reason.Contains(
                   "Gemini trả JSON chưa hoàn chỉnh",
                   StringComparison.OrdinalIgnoreCase) ||
               reason.Contains(
                   "Gemini chưa trả được JSON",
                   StringComparison.OrdinalIgnoreCase) ||
               reason.Contains(
                   "Gemini planning chưa trả",
                   StringComparison.OrdinalIgnoreCase) ||
               reason.Contains(
                   "Gemini planning không trả",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static DesktopOperatorDecision CreatePlannerCooldownWaitDecision(
        DateTimeOffset backoffUntil) =>
        new(
            State: "Provider semantic đang cooldown; scene hiện tại chưa đổi.",
            Plan: "Không gọi lại provider trên cùng scene. Chờ ngắn hoặc dùng local capability nếu có.",
            CurrentSubgoal: string.Empty,
            GoalProgress: 0,
            VerifiedMilestones: Array.Empty<string>(),
            Action: "wait",
            Query: string.Empty,
            Text: string.Empty,
            Key: string.Empty,
            Keys: Array.Empty<string>(),
            Url: string.Empty,
            TargetLabel: string.Empty,
            CoordinateSpace: ComputerCoordinateSpaces.ImagePixel,
            CoordinateWindowId: string.Empty,
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
            ExpectedEffect: string.Empty,
            Confidence: 1.0,
            Reason: $"Gemini đang cooldown đến {backoffUntil:HH:mm:ss}; không gọi lặp khi scene chưa thay đổi.",
            SceneElements: Array.Empty<DesktopSceneElement>(),
            TargetElementId: string.Empty);

    private static bool IsTransientVisionFailure(
        HttpRequestException exception)
    {
        if (exception.StatusCode is null)
            return true;

        var code = (int)exception.StatusCode.Value;
        return code is 408 or 429 or 500 or 502 or 503 or 504;
    }

    private async Task<ActionVerificationResult?> WaitForAdaptiveTransitionAsync(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame referenceFrame,
        VerificationCaptureContext? verificationCaptureContext,
        DesktopFastObserverSample referenceSample,
        CancellationToken cancellationToken)
    {
        var policy =
            ComputerOperatorAdaptiveWaitPolicy.ForAction(
                decision.Action);

        progress.Add(
            "adaptive-wait",
            $"Chưa có kết quả cuối cùng. Bắt đầu chờ thích ứng: stall={policy.StallTimeout.TotalSeconds:0}s; tối đa={policy.AbsoluteTimeout.TotalSeconds:0}s. Trong thời gian này KHÔNG thực thi lại action.",
            AdaptiveWaitStatuses.Pending,
            decision.Confidence);

        using var adaptiveWaitTelemetry =
            telemetry.Begin(
                ComputerOperatorTelemetryStages.AdaptiveWait,
                decision.Action);

        var result = await adaptiveWait.WaitAsync(
            async token =>
            {
                await execution.WaitIfPausedAsync(token);

                var frame =
                    await CapturePostActionFrameAsync(
                        verificationCaptureContext,
                        token);

                try
                {
                    var contextChanged =
                        verificationCaptureContext is not null &&
                        !verificationCaptureContext.Matches(frame);

                    var difference =
                        contextChanged
                            ? null
                            : frameDifferences.Compare(
                                referenceFrame,
                                frame);

                    if (contextChanged)
                    {
                        progress.Add(
                            "capture-context-changed",
                            "Adaptive Wait phát hiện capture context thay đổi; không so pixel giữa hai geometry khác nhau. Đây là transition evidence.",
                            "transition",
                            observation: true);
                    }

                    var currentSample =
                        fastObserver.CaptureSample(frame);

                    var observation =
                        fastObserver.Analyze(
                            referenceSample,
                            currentSample,
                            difference);

                    var route =
                        VerificationRouter.Route(
                            decision,
                            observation,
                            difference);

                    var sample =
                        ComputerOperatorAdaptiveWaitPolicy
                            .FromDesktopObservation(
                                observation,
                                difference,
                                route);

                    var trangThai = sample.Status switch
                    {
                        AdaptiveWaitStatuses.Verified =>
                            "Đã xác minh",
                        AdaptiveWaitStatuses.Progressing =>
                            "Đang tiến triển",
                        AdaptiveWaitStatuses.Pending =>
                            "Đang chờ",
                        AdaptiveWaitStatuses.Stalled =>
                            "Bị đình trệ",
                        AdaptiveWaitStatuses.Failed =>
                            "Thất bại",
                        _ => sample.Status
                    };

                    progress.Add(
                        "adaptive-wait",
                        $"{trangThai}: {sample.Reason}",
                        sample.Status,
                        sample.Confidence);

                    return sample;
                }
                finally
                {
                    frame.Clear();
                }
            },
            policy,
            verificationCaptureContext?.WindowId,
            decision.Action,
            decision.ExpectedEffect,
            cancellationToken);

        adaptiveWaitTelemetry.Complete(
            result.Verified,
            result.Status);

        progress.Add(
            "adaptive-wait-result",
            result.Status switch
            {
                AdaptiveWaitStatuses.Verified =>
                    $"Đã xác minh sau {result.Elapsed.TotalSeconds:0.0}s; samples={result.Samples}; heartbeat={result.ProgressHeartbeats}; eventWakeups={result.EventWakeups}; ignoredEvents={result.IgnoredEventWakeups}. {result.Reason}",
                AdaptiveWaitStatuses.Stalled =>
                    $"Chưa có tiến triển đủ mạnh sau {result.Elapsed.TotalSeconds:0.0}s; chưa kết luận action thất bại. Chuyển sang semantic verifier. {result.Reason}",
                AdaptiveWaitStatuses.Failed =>
                    $"Có bằng chứng thất bại rõ ràng sau {result.Elapsed.TotalSeconds:0.0}s. {result.Reason}",
                _ =>
                    $"{result.Status}: {result.Reason}"
            },
            result.Status);

        if (result.Verified)
        {
            return new(
                true,
                0.97,
                $"Adaptive Wait xác minh kết quả mà không replay action: {result.Reason}");
        }

        if (result.Failed)
        {
            return new(
                false,
                0.97,
                $"Adaptive Wait có bằng chứng thất bại rõ ràng: {result.Reason}");
        }

        // Stalled/Pending không đồng nghĩa Failed. Caller sẽ dùng frame mới
        // nhất và semantic verifier trước khi được phép replan.
        return null;
    }

    private async Task<DesktopScreenshotFrame> CapturePostActionFrameAsync(
        VerificationCaptureContext? context,
        CancellationToken cancellationToken)
    {
        if (context is null)
            return await CapturePostActionFrameAsync(cancellationToken);

        try
        {
            if (context.CaptureScope.Equals(
                    "window",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(context.WindowId))
            {
                return await screenshots.CaptureStableWindowAsync(
                    context.WindowId,
                    maximumWaitMs: 5000,
                    cancellationToken);
            }

            if (context.CaptureScope.Equals(
                    "monitor",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(context.MonitorDevice))
            {
                return await screenshots.CaptureStableMonitorAsync(
                    context.MonitorDevice,
                    maximumWaitMs: 5000,
                    cancellationToken);
            }

            if (context.CaptureScope.Equals(
                    "virtual-desktop",
                    StringComparison.OrdinalIgnoreCase))
            {
                return await screenshots.CaptureStableVirtualScreenAsync(
                    maximumWaitMs: 5000,
                    cancellationToken);
            }
        }
        catch (Exception exception) when (
            exception is ToolExecutionInputException or
            InvalidOperationException)
        {
            logger.LogDebug(
                exception,
                "Không giữ được Verification Capture Context; fallback về capture động rồi đánh dấu context changed.");
        }

        return await CapturePostActionFrameAsync(
            cancellationToken);
    }

    private async Task<DesktopScreenshotFrame> CapturePostActionFrameAsync(
        CancellationToken cancellationToken)
    {
        var active = computer.GetActiveWindow();

        if (active is not null &&
            active.Width >= 64 &&
            active.Height >= 64)
        {
            try
            {
                return await screenshots.CaptureStableWindowAsync(
                    active.WindowId,
                    maximumWaitMs: 5000,
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is ToolExecutionInputException or
                InvalidOperationException)
            {
                logger.LogDebug(
                    exception,
                    "Không chụp ổn định được foreground window {WindowId}; thử monitor theo con trỏ.",
                    active.WindowId);
            }
        }

        try
        {
            var cursor = computer.GetCursorPosition();
            var monitor = displays.GetMonitorAtPoint(
                cursor.X,
                cursor.Y);

            if (monitor is not null)
            {
                return await screenshots.CaptureStableMonitorAsync(
                    monitor.DeviceName,
                    maximumWaitMs: 5000,
                    cancellationToken);
            }
        }
        catch (Exception exception) when (
            exception is ToolExecutionInputException or
            InvalidOperationException)
        {
            logger.LogDebug(
                exception,
                "Không chụp ổn định được monitor theo con trỏ; fallback về virtual desktop.");
        }

        return await screenshots.CaptureStableVirtualScreenAsync(
            maximumWaitMs: 5000,
            cancellationToken);
    }

    private ComputerWindowInfo? ResolveTrackingWindow(
        DesktopOperatorDecision decision,
        ComputerWindowInfo? activeAtPlanning,
        DesktopScreenshotFrame planningFrame)
    {
        if (!string.IsNullOrWhiteSpace(decision.CoordinateWindowId))
        {
            return computer.GetWindows(50).Windows.FirstOrDefault(
                window => window.WindowId.Equals(
                    decision.CoordinateWindowId.Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }

        if (activeAtPlanning is null)
            return null;

        if (decision.BoxWidth <= 1 ||
            decision.BoxHeight <= 1)
            return activeAtPlanning;

        var centerX =
            planningFrame.Left +
            decision.BoxLeft +
            decision.BoxWidth / 2;
        var centerY =
            planningFrame.Top +
            decision.BoxTop +
            decision.BoxHeight / 2;

        var insideActive =
            centerX >= activeAtPlanning.Left &&
            centerX < activeAtPlanning.Left + activeAtPlanning.Width &&
            centerY >= activeAtPlanning.Top &&
            centerY < activeAtPlanning.Top + activeAtPlanning.Height;

        return insideActive
            ? activeAtPlanning
            : null;
    }

    private ComputerOperatorCheckpoint SaveCheckpointSafely(
        ComputerOperatorCheckpoint checkpoint,
        IReadOnlyCollection<string> verifiedMilestones,
        string currentSubgoal,
        double goalProgress,
        string? lastVerifiedAction = null,
        string? lastVerifiedExpectedEffect = null)
    {
        try
        {
            return checkpoints.SaveProgress(
                checkpoint,
                verifiedMilestones,
                currentSubgoal,
                goalProgress,
                lastVerifiedAction,
                lastVerifiedExpectedEffect);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Không lưu được Computer Operator checkpoint {CheckpointId}.",
                checkpoint.Id);
            return checkpoint;
        }
    }

    private void MarkCheckpointStatusSafely(
        ComputerOperatorCheckpoint checkpoint,
        string status)
    {
        try
        {
            checkpoints.MarkStatus(
                checkpoint,
                status);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Không cập nhật được trạng thái Computer Operator checkpoint {CheckpointId}.",
                checkpoint.Id);
        }
    }

    private void RecordOutcomeLoop(
        ComputerOperatorLoopGuardSession loopGuard,
        ICollection<string> taskHistory,
        DesktopOperatorDecision decision,
        string sceneFingerprint,
        string actionSignature,
        string outcome)
    {
        var assessment = loopGuard.ObserveOutcome(
            sceneFingerprint,
            actionSignature,
            outcome);

        if (!assessment.Detected)
            return;

        taskHistory.Add(
            $"ANTI-LOOP {assessment.Kind}: {assessment.Detail}");

        progress.Add(
            "anti-loop",
            $"Anti-loop: {assessment.Detail} Cảnh báo tích lũy: {assessment.Occurrences}.",
            "replan",
            decision.Confidence);
    }

    private static string ClassifyFailureKind(
        string action,
        string detail,
        bool actionApplied,
        bool verificationFailed)
    {
        var normalizedAction = (action ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        var normalizedDetail = (detail ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (normalizedDetail.Contains("target") &&
            (normalizedDetail.Contains("không còn") ||
             normalizedDetail.Contains("not found") ||
             normalizedDetail.Contains("biến mất")))
            return ComputerOperatorFailureTaxonomy.TargetNotFound;

        if (normalizedDetail.Contains("occlud") ||
            normalizedDetail.Contains("bị che") ||
            normalizedDetail.Contains("popup"))
            return normalizedDetail.Contains("popup")
                ? ComputerOperatorFailureTaxonomy.UnexpectedPopup
                : ComputerOperatorFailureTaxonomy.TargetOccluded;

        if (normalizedDetail.Contains("foreground") ||
            normalizedDetail.Contains("focus"))
            return ComputerOperatorFailureTaxonomy.WrongFocus;

        if (normalizedDetail.Contains("window") &&
            normalizedDetail.Contains("khác"))
            return ComputerOperatorFailureTaxonomy.WrongWindow;

        if (normalizedDetail.Contains("dpi") ||
            normalizedDetail.Contains("monitor") ||
            normalizedDetail.Contains("resolution"))
            return ComputerOperatorFailureTaxonomy.ResolutionChanged;

        if (normalizedAction == "scroll" &&
            (!actionApplied || verificationFailed))
            return ComputerOperatorFailureTaxonomy.ScrollNoEffect;

        if ((normalizedAction == "type-text" ||
             normalizedAction == "press-key" ||
             normalizedAction == "press-hotkey") &&
            (!actionApplied || verificationFailed))
            return ComputerOperatorFailureTaxonomy.KeyboardRejected;

        if (normalizedDetail.Contains("timeout") ||
            normalizedDetail.Contains("loading") ||
            normalizedDetail.Contains("đang tải"))
            return ComputerOperatorFailureTaxonomy.LoadingTimeout;

        if (!actionApplied || verificationFailed)
            return ComputerOperatorFailureTaxonomy.ActionNoEffect;

        return ComputerOperatorFailureTaxonomy.Unknown;
    }

    private static bool IsKeyboardAction(
        string action) =>
        action is
            "type-text" or
            "press-key" or
            "press-hotkey";

    private static bool IsFineGrainedKeyboardRepair(
        DesktopOperatorDecision decision)
    {
        if (!decision.Action.Equals(
                "press-key",
                StringComparison.OrdinalIgnoreCase))
            return false;

        var key = (decision.Key ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        return key is "BACKSPACE" or "DELETE";
    }

    private static bool IsSelectAllHotkey(
        DesktopOperatorDecision decision)
    {
        if (!decision.Action.Equals(
                "press-hotkey",
                StringComparison.OrdinalIgnoreCase) ||
            decision.Keys.Count != 2)
            return false;

        var keys = decision.Keys
            .Select(key => (key ?? string.Empty).Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return keys.Contains("CTRL") &&
               keys.Contains("A");
    }

    private static bool RequiresExpectedEffect(
        string action) =>
        action is not
            "wait" and not
            "complete" and not
            "blocked" and not
            "move-pointer";

    private ComputerActionResponse ExecuteDecision(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame frame)
    {
        var active = computer.GetActiveWindow();

        ComputerCoordinatePoint? point = null;
        ComputerCoordinatePoint? endPoint = null;
        ComputerSafeTargetPoint? safeTarget = null;

        if (IsClickAction(decision.Action))
        {
            safeTarget = targeting.Resolve(
                decision,
                frame);
            point = safeTarget.Point;
        }
        else if (IsPointerAction(decision.Action))
        {
            point = coordinates.ToDesktopPoint(
                BuildCoordinateRequest(decision, useEnd: false),
                frame);
        }

        if (decision.Action.Equals(
                "drag-left",
                StringComparison.OrdinalIgnoreCase))
        {
            endPoint = coordinates.ToDesktopPoint(
                BuildCoordinateRequest(decision, useEnd: true),
                frame);
        }

        return decision.Action switch
        {
            "move-pointer" => computer.SmoothMoveCursor(
                RequirePoint(point).DesktopX,
                RequirePoint(point).DesktopY,
                420),
            "click-left" => ExecutePointerClick(
                decision,
                RequirePoint(point),
                RequireSafeTarget(safeTarget),
                static (computerUse, windowId, x, y) =>
                    computerUse.ClickLeft(windowId, x, y)),
            "double-click-left" => ExecutePointerClick(
                decision,
                RequirePoint(point),
                RequireSafeTarget(safeTarget),
                static (computerUse, windowId, x, y) =>
                    computerUse.DoubleClickLeft(windowId, x, y)),
            "click-right" => ExecutePointerClick(
                decision,
                RequirePoint(point),
                RequireSafeTarget(safeTarget),
                static (computerUse, windowId, x, y) =>
                    computerUse.ClickRight(windowId, x, y)),
            "scroll" => ExecutePointerScroll(
                decision,
                RequirePoint(point)),
            "drag-left" => ExecutePointerDrag(
                decision,
                RequirePoint(point),
                RequirePoint(endPoint)),
            "focus-window" => computer.FocusWindowByQuery(
                RequireValue(decision.Query, "query")),
            "minimize" => computer.MinimizeWindow(
                RequireActive(active)),
            "maximize" => computer.MaximizeWindow(
                RequireActive(active)),
            "restore" => computer.RestoreWindow(
                RequireActive(active)),
            "type-text" => computer.TypeText(
                RequireActive(active),
                RequireValue(decision.Text, "text")),
            "press-key" => computer.PressKey(
                RequireActive(active),
                RequireValue(decision.Key, "key")),
            "press-hotkey" => computer.PressHotkey(
                RequireActive(active),
                decision.Keys.Count > 0
                    ? decision.Keys
                    : throw new ToolExecutionInputException(
                        "Vision không trả danh sách hotkey.")),
            "open-browser" => computer.OpenDefaultBrowser(
                string.IsNullOrWhiteSpace(decision.Url)
                    ? null
                    : decision.Url),
            _ => throw new ToolExecutionInputException(
                $"Computer Operator trả hành động không được hỗ trợ: {decision.Action}.")
        };
    }

    private ComputerActionResponse ExecutePointerClick(
        DesktopOperatorDecision decision,
        ComputerCoordinatePoint point,
        ComputerSafeTargetPoint safeTarget,
        Func<IComputerUseService, string, int, int, ComputerActionResponse> click)
    {
        var desktopX = point.DesktopX;
        var desktopY = point.DesktopY;

        var targetWindow = computer.GetWindowAtPoint(
            desktopX,
            desktopY)
            ?? throw new ToolExecutionInputException(
                $"Không xác định được cửa sổ tại tọa độ ({desktopX}, {desktopY}).");

        _ = computer.SmoothMoveCursor(
            desktopX,
            desktopY,
            420);

        var result = click(
            computer,
            targetWindow.WindowId,
            desktopX,
            desktopY);

        return result with
        {
            Detail =
                $"{result.Detail} Target={DescribeTarget(decision)} tại ({desktopX}, {desktopY}); hệ={point.Space}; nguồn={point.SourceDescription}; {safeTarget.Detail}"
        };
    }

    private ComputerActionResponse ExecutePointerScroll(
        DesktopOperatorDecision decision,
        ComputerCoordinatePoint point)
    {
        var desktopX = point.DesktopX;
        var desktopY = point.DesktopY;

        var targetWindow = computer.GetWindowAtPoint(
            desktopX,
            desktopY)
            ?? throw new ToolExecutionInputException(
                $"Không xác định được cửa sổ tại tọa độ cuộn ({desktopX}, {desktopY}).");

        return computer.Scroll(
            targetWindow.WindowId,
            desktopX,
            desktopY,
            decision.ScrollDelta);
    }

    private ComputerActionResponse ExecutePointerDrag(
        DesktopOperatorDecision decision,
        ComputerCoordinatePoint start,
        ComputerCoordinatePoint end)
    {
        var startX = start.DesktopX;
        var startY = start.DesktopY;
        var endX = end.DesktopX;
        var endY = end.DesktopY;

        var targetWindow = computer.GetWindowAtPoint(
            startX,
            startY)
            ?? throw new ToolExecutionInputException(
                $"Không xác định được cửa sổ tại điểm bắt đầu kéo ({startX}, {startY}).");

        return computer.DragLeft(
            targetWindow.WindowId,
            startX,
            startY,
            endX,
            endY,
            650);
    }

    private static bool IsClickAction(
        string action) =>
        action is
            "click-left" or
            "double-click-left" or
            "click-right";

    private static bool IsPointerAction(
        string action) =>
        action is
            "move-pointer" or
            "click-left" or
            "double-click-left" or
            "click-right" or
            "scroll" or
            "drag-left";

    private static ComputerCoordinateRequest BuildCoordinateRequest(
        DesktopOperatorDecision decision,
        bool useEnd)
    {
        var space = string.IsNullOrWhiteSpace(decision.CoordinateSpace)
            ? ComputerCoordinateSpaces.ImagePixel
            : decision.CoordinateSpace.Trim().ToLowerInvariant();

        return new(
            space,
            useEnd ? decision.EndImageX : decision.ImageX,
            useEnd ? decision.EndImageY : decision.ImageY,
            useEnd ? decision.EndNormalizedX : decision.NormalizedX,
            useEnd ? decision.EndNormalizedY : decision.NormalizedY,
            decision.CoordinateWindowId);
    }

    private static ComputerCoordinatePoint RequirePoint(
        ComputerCoordinatePoint? point) =>
        point ?? throw new ToolExecutionInputException(
            "Không có tọa độ đã chuyển đổi cho hành động chuột.");

    private static ComputerSafeTargetPoint RequireSafeTarget(
        ComputerSafeTargetPoint? target) =>
        target ?? throw new ToolExecutionInputException(
            "Không có vùng mục tiêu an toàn cho hành động click.");

    private static string DescribeTarget(
        DesktopOperatorDecision decision) =>
        string.IsNullOrWhiteSpace(decision.TargetLabel)
            ? "phần tử nhìn thấy"
            : decision.TargetLabel.Trim();

    private static string BuildHistoryContext(
        IReadOnlyList<string> history,
        ComputerOperatorRecoverySession recovery,
        IReadOnlyCollection<string> verifiedMilestones,
        string currentSubgoal,
        double goalProgress)
    {
        var historyText = history.Count == 0
            ? "(chưa có hành động trước đó)"
            : string.Join(
                "\n",
                history.TakeLast(14));

        var milestonesText = verifiedMilestones.Count == 0
            ? "(chưa có mốc đã xác minh)"
            : string.Join(
                "\n",
                verifiedMilestones
                    .TakeLast(10)
                    .Select(item => $"- {item}"));

        return
            historyText +
            "\n\nMỤC TIÊU CON TRƯỚC ĐÓ: " +
            (string.IsNullOrWhiteSpace(currentSubgoal)
                ? "(chưa xác định)"
                : currentSubgoal) +
            $"\nTIẾN ĐỘ BÁO CÁO TRƯỚC ĐÓ: {goalProgress * 100:0}%\n" +
            "CÁC MỐC ĐÃ XÁC MINH:\n" +
            milestonesText +
            "\n\n" +
            recovery.BuildContext();
    }

    private static string BuildSemanticActionSignature(
        DesktopOperatorDecision decision)
    {
        static string Clip(string? value, int length)
        {
            var normalized = string.Join(
                " ",
                (value ?? string.Empty)
                    .Trim()
                    .ToLowerInvariant()
                    .Split(
                        [' ', '\t', '\r', '\n', '.', ',', ';', ':'],
                        StringSplitOptions.RemoveEmptyEntries));

            return normalized.Length <= length
                ? normalized
                : normalized[..length];
        }

        var action = (decision.Action ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        var targetIdentity =
            !string.IsNullOrWhiteSpace(decision.TargetElementId)
                ? $"id={Clip(decision.TargetElementId, 80)}"
                : !string.IsNullOrWhiteSpace(decision.TargetLabel)
                    ? $"label={Clip(decision.TargetLabel, 120)}"
                    : $"coarse={CoarseCoordinateSignature(decision)}";

        var expected =
            Clip(decision.ExpectedEffect, 180);

        return action switch
        {
            "click-left" or
            "double-click-left" or
            "click-right" =>
                $"{action}|{targetIdentity}|effect={expected}",

            "scroll" =>
                $"scroll|target={targetIdentity}|direction={Math.Sign(decision.ScrollDelta)}|effect={expected}",

            "drag-left" =>
                $"drag-left|target={targetIdentity}|effect={expected}",

            "focus-window" =>
                $"focus-window|query={Clip(decision.Query, 100)}|effect={expected}",

            "type-text" =>
                $"type-text|target={targetIdentity}|effect={expected}",

            "press-key" =>
                $"press-key|key={Clip(decision.Key, 24)}|effect={expected}",

            "press-hotkey" =>
                $"press-hotkey|keys={string.Join("+", decision.Keys.Select(key => Clip(key, 24)))}|effect={expected}",

            "open-browser" =>
                $"open-browser|url={Clip(decision.Url, 120)}|effect={expected}",

            "move-pointer" =>
                $"move-pointer|{targetIdentity}",

            _ => $"{action}|{targetIdentity}|effect={expected}"
        };
    }

    private static string CoarseCoordinateSignature(
        DesktopOperatorDecision decision)
    {
        var x = Math.Clamp(
            decision.NormalizedX > 0
                ? decision.NormalizedX
                : decision.ImageX / 1920.0,
            0,
            1);
        var y = Math.Clamp(
            decision.NormalizedY > 0
                ? decision.NormalizedY
                : decision.ImageY / 1080.0,
            0,
            1);

        var bucketX = (int)Math.Floor(x * 8);
        var bucketY = (int)Math.Floor(y * 6);

        return $"{Math.Clamp(bucketX, 0, 7)},{Math.Clamp(bucketY, 0, 5)}";
    }

    private static string CoordinateSignature(
        DesktopOperatorDecision decision,
        bool useEnd)
    {
        var space = string.IsNullOrWhiteSpace(decision.CoordinateSpace)
            ? ComputerCoordinateSpaces.ImagePixel
            : decision.CoordinateSpace.Trim().ToLowerInvariant();

        if (space == ComputerCoordinateSpaces.ImagePixel)
        {
            return useEnd
                ? $"{space}:{decision.EndImageX},{decision.EndImageY}"
                : $"{space}:{decision.ImageX},{decision.ImageY}";
        }

        var x = useEnd
            ? decision.EndNormalizedX
            : decision.NormalizedX;
        var y = useEnd
            ? decision.EndNormalizedY
            : decision.NormalizedY;

        return space == ComputerCoordinateSpaces.WindowNormalized
            ? $"{space}:{decision.CoordinateWindowId}:{x:0.0000},{y:0.0000}"
            : $"{space}:{x:0.0000},{y:0.0000}";
    }


    private static string BuildDiagnosticId(
        string value)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                value ?? string.Empty));

        return Convert.ToHexString(bytes)[..12];
    }

    private static string LimitDiagnostic(
        string? value,
        int maximum)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= maximum
            ? normalized
            : normalized[..Math.Max(1, maximum - 1)] + "…";
    }

    private static string BuildDesktopSceneFingerprint(
        ComputerOperatorDesktopState state)
    {
        var foreground = state.ForegroundWindow is null
            ? "foreground:none"
            : $"foreground:{NormalizeSceneToken(state.ForegroundWindow.ProcessName)}|{NormalizeSceneToken(state.ForegroundWindow.Title)}|{Quantize(state.ForegroundWindow.Left, 32)},{Quantize(state.ForegroundWindow.Top, 32)},{Quantize(state.ForegroundWindow.Width, 32)},{Quantize(state.ForegroundWindow.Height, 32)}";

        var visible = state.Windows
            .Where(window => window.Width > 0 && window.Height > 0)
            .OrderBy(window => window.ProcessName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .Select(window =>
                $"{NormalizeSceneToken(window.ProcessName)}:{NormalizeSceneToken(window.Title)}:{Quantize(window.Left, 64)},{Quantize(window.Top, 64)},{Quantize(window.Width, 64)},{Quantize(window.Height, 64)}");

        return string.Join(
            "|",
            new[]
            {
                foreground,
                $"frame:{state.CaptureScope}:{Quantize(state.FrameLeft, 32)},{Quantize(state.FrameTop, 32)},{Quantize(state.FrameWidth, 32)},{Quantize(state.FrameHeight, 32)}",
                $"windows:{string.Join(";", visible)}"
            });
    }

    private static string NormalizeSceneToken(
        string? value)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= 80
            ? normalized
            : normalized[..80];
    }

    private static int Quantize(
        int value,
        int quantum) =>
        quantum <= 1
            ? value
            : (int)Math.Round(
                value / (double)quantum,
                MidpointRounding.AwayFromZero) * quantum;

    private ComputerOperatorDesktopState BuildDesktopState(
        DesktopScreenshotFrame frame)
    {
        var windows = computer.GetWindows(50).Windows;
        var active = windows.FirstOrDefault(window => window.IsForeground)
            ?? computer.GetActiveWindow();

        return new ComputerOperatorDesktopState(
            frame.CapturedAtUtc,
            active,
            windows,
            frame.Left,
            frame.Top,
            frame.Width,
            frame.Height,
            frame.CaptureScope,
            frame.WindowId,
            frame.WindowWasForeground);
    }

    private static string RequireActive(
        ComputerWindowInfo? active)
    {
        return active?.WindowId
            ?? throw new ToolExecutionInputException(
                "Không xác định được cửa sổ foreground cho thao tác này.");
    }

    private static string RequireValue(
        string value,
        string name)
    {
        return !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ToolExecutionInputException(
                $"Vision không trả tham số {name} cần thiết.");
    }
}
