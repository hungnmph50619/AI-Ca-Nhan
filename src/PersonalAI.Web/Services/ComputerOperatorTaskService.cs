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
    ILogger<ComputerOperatorTaskService> logger)
    : IComputerOperatorTaskService
{
    private const int MaximumSteps = 12;
    private const double MinimumConfidence = 0.72;

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

        control.EnableScopedAutomation(
            maximumActions: 12,
            maximumSeconds: 90);

        var operatorToken = execution.Begin(normalizedGoal);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            operatorToken);

        progress.Start(
            $"Bắt đầu tác vụ: {normalizedGoal}");

        var steps = new List<ComputerOperatorTaskStep>();
        var taskHistory = new List<string>();
        var recovery = new ComputerOperatorRecoverySession();
        var loopGuard = new ComputerOperatorLoopGuardSession();
        var verifiedMilestones = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var currentSubgoal = string.Empty;
        var latestGoalProgress = 0.0;
        var lowConfidenceCount = 0;

        try
        {
            for (var index = 1; index <= MaximumSteps; index++)
            {
                linked.Token.ThrowIfCancellationRequested();

                if (control.GetStatus().Paused)
                    return Finish(
                        false,
                        "Computer Operator đã dừng vì phiên điều khiển hết hạn, hết ngân sách hoặc bị dừng khẩn cấp.");

                await execution.WaitIfPausedAsync(linked.Token);

                var windowsContext = BuildObservation();
                var active = computer.GetActiveWindow();

                progress.Add(
                    "stabilize",
                    active is null
                        ? "Đang chờ desktop ổn định trước khi quan sát. Foreground chưa xác định."
                        : $"Đang chờ desktop ổn định trước khi quan sát. Foreground: {active.Title}.");

                DesktopScreenshotFrame frame;
                try
                {
                    frame = await screenshots.CaptureStableVirtualScreenAsync(
                        maximumWaitMs: 5000,
                        linked.Token);

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
                    progress.Block(
                        $"Không chụp được desktop: {exception.Message}");
                    return Finish(
                        false,
                        $"Không chụp được desktop: {exception.Message}");
                }

                DesktopOperatorDecision decision;
                try
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
                        linked.Token);
                }
                finally
                {
                    frame.Clear();
                }

                if (!string.IsNullOrWhiteSpace(decision.CurrentSubgoal) &&
                    !decision.CurrentSubgoal.Equals(
                        currentSubgoal,
                        StringComparison.OrdinalIgnoreCase))
                {
                    currentSubgoal = decision.CurrentSubgoal.Trim();
                    taskHistory.Add(
                        $"SUBGOAL: {currentSubgoal}");
                    progress.Add(
                        "subgoal",
                        $"MỤC TIÊU CON: {currentSubgoal}",
                        "plan",
                        decision.Confidence);
                }

                latestGoalProgress = decision.GoalProgress;

                progress.Add(
                    "goal-progress",
                    $"TIẾN ĐỘ MỤC TIÊU: khoảng {latestGoalProgress * 100:0}%.",
                    "plan",
                    decision.Confidence);

                foreach (var milestone in decision.VerifiedMilestones)
                {
                    var normalizedMilestone = milestone.Trim();
                    if (normalizedMilestone.Length == 0 ||
                        !verifiedMilestones.Add(normalizedMilestone))
                        continue;

                    taskHistory.Add(
                        $"MILESTONE-OBSERVED: {normalizedMilestone}");
                    progress.Add(
                        "milestone",
                        $"MỐC ĐÃ XÁC MINH: {normalizedMilestone}",
                        "verified",
                        decision.Confidence);
                }

                var loopStrategy =
                    decision.Action is "complete" or "blocked" or "wait"
                        ? string.Empty
                        : BuildActionSignature(decision);

                var loopAssessment = loopGuard.Observe(
                    decision.State,
                    loopStrategy);

                progress.Add(
                    "state",
                    string.IsNullOrWhiteSpace(decision.State)
                        ? "AI đã cập nhật trạng thái desktop."
                        : $"STATE: {decision.State}");

                if (loopAssessment.Detected)
                {
                    taskHistory.Add(
                        $"LOOP-WARNING {loopAssessment.Kind}: {loopAssessment.Detail}");

                    progress.Add(
                        "loop-detected",
                        $"Phát hiện nguy cơ vòng lặp: {loopAssessment.Detail} Cảnh báo tích lũy: {loopAssessment.Occurrences}.",
                        "replan",
                        decision.Confidence);

                    if (loopAssessment.RequiresStrategyChange)
                    {
                        taskHistory.Add(
                            "LOOP-DIRECTIVE: BẮT BUỘC đổi chiến lược. Không lặp lại cùng action/target. Nếu không còn phương án an toàn hợp lý, trả blocked và giải thích.");

                        progress.Add(
                            "replan",
                            "Chiến lược hiện tại đã nằm trong vòng lặp; không thực hiện lại. AI phải quan sát trạng thái hiện tại và chọn chiến lược khác.",
                            "replan",
                            decision.Confidence);

                        await Task.Delay(250, linked.Token);
                        continue;
                    }
                }

                progress.Add(
                    "plan",
                    string.IsNullOrWhiteSpace(decision.Plan)
                        ? "AI đang chọn bước tiếp theo từ trạng thái hiện tại."
                        : $"PLAN: {decision.Plan}",
                    decision.Action,
                    decision.Confidence);

                progress.Add(
                    "decide",
                    $"DECIDE: {decision.Reason}",
                    decision.Action,
                    decision.Confidence);

                if (decision.Action == "complete")
                {
                    taskHistory.Add(
                        $"STEP {index}: COMPLETE — {decision.Reason}");
                    progress.Complete(
                        $"Vision xác nhận mục tiêu đã đạt: {decision.Reason}");
                    return Finish(
                        true,
                        steps.Count == 0
                            ? "Vision xác nhận mục tiêu đã ở trạng thái hoàn thành."
                            : $"Vision xác nhận tác vụ hoàn thành sau {steps.Count} bước.");
                }

                if (decision.Action == "blocked")
                {
                    taskHistory.Add(
                        $"STEP {index}: BLOCKED — {decision.Reason}");
                    progress.Block(
                        $"Vision dừng an toàn sau khi xem trạng thái và lịch sử: {decision.Reason}");
                    return Finish(
                        false,
                        $"Vision dừng an toàn: {decision.Reason}");
                }

                if (decision.Action == "wait")
                {
                    taskHistory.Add(
                        $"STEP {index}: WAIT — {decision.Reason}");
                    progress.Add(
                        "wait",
                        $"Vision yêu cầu chờ rồi quan sát lại: {decision.Reason}",
                        "wait",
                        decision.Confidence);
                    await Task.Delay(900, linked.Token);
                    continue;
                }

                if (decision.Confidence < MinimumConfidence)
                {
                    lowConfidenceCount++;
                    progress.Add(
                        "analyze-retry",
                        $"Độ tin cậy {decision.Confidence:0.00} thấp hơn {MinimumConfidence:0.00}; sẽ quan sát lại ({lowConfidenceCount}/3).",
                        decision.Action,
                        decision.Confidence);

                    taskHistory.Add(
                        $"STEP {index}: LOW-CONFIDENCE {decision.Action} ({decision.Confidence:0.00}) — {decision.Reason}");

                    recovery.RecordFailure(
                        BuildActionSignature(decision),
                        decision.Action,
                        ComputerOperatorFailureKinds.LowConfidence,
                        decision.State,
                        decision.Reason,
                        decision.ExpectedEffect,
                        decision.Confidence);

                    if (lowConfidenceCount >= 3)
                    {
                        progress.Block(
                            "Vision không đủ chắc chắn sau 3 lần quan sát.");
                        return Finish(
                            false,
                            "Vision không đủ chắc chắn sau 3 lần quan sát.");
                    }

                    await Task.Delay(700, linked.Token);
                    continue;
                }

                lowConfidenceCount = 0;

                if (RequiresExpectedEffect(decision.Action) &&
                    string.IsNullOrWhiteSpace(decision.ExpectedEffect))
                {
                    taskHistory.Add(
                        $"STEP {index}: REJECTED {decision.Action} — thiếu EXPECTED EFFECT để xác minh.");

                    recovery.RecordFailure(
                        BuildActionSignature(decision),
                        decision.Action,
                        ComputerOperatorFailureKinds.MissingExpectedEffect,
                        decision.State,
                        "Thiếu kết quả mong đợi để xác minh hành động.",
                        decision.ExpectedEffect,
                        decision.Confidence);

                    progress.Add(
                        "recovery",
                        "AI chưa nêu kết quả mong đợi có thể kiểm tra cho hành động này; không thực hiện và sẽ lập lại phương án.",
                        decision.Action,
                        decision.Confidence);

                    await Task.Delay(300, linked.Token);
                    continue;
                }

                var actionSignature = BuildActionSignature(decision);

                if (recovery.ShouldAvoidRepeatedStrategy(
                        actionSignature,
                        decision.State,
                        out var avoidReason))
                {
                    taskHistory.Add(
                        $"STEP {index}: RECOVERY-SKIP {actionSignature} — {avoidReason}");

                    progress.Add(
                        "replan",
                        $"Không lặp lại chiến lược vừa thất bại: {avoidReason}",
                        decision.Action,
                        decision.Confidence);

                    await Task.Delay(250, linked.Token);
                    continue;
                }

                progress.Add(
                    "act",
                    $"Chuẩn bị thực hiện: {decision.Action}. {decision.Reason}",
                    decision.Action,
                    decision.Confidence);

                DesktopScreenshotFrame? verificationBaseline = null;
                try
                {
                    verificationBaseline = await CapturePostActionFrameAsync(linked.Token);
                    progress.Add(
                        "frame-baseline",
                        $"Đã chụp baseline trước hành động {verificationBaseline.Width}x{verificationBaseline.Height}; scope={verificationBaseline.CaptureScope}.",
                        observation: true);
                }
                catch (Exception exception) when (
                    exception is ToolExecutionInputException or
                    InvalidOperationException)
                {
                    logger.LogDebug(
                        exception,
                        "Không chụp được baseline frame difference; tiếp tục xác minh bằng ảnh hậu hành động.");
                }

                ComputerActionResponse action;
                try
                {
                    await execution.WaitIfPausedAsync(linked.Token);
                    action = ExecuteDecision(decision, frame);
                }
                catch (ToolExecutionInputException exception)
                {
                    verificationBaseline?.Clear();
                    logger.LogWarning(
                        "Computer Operator step {Step} rejected: {Reason}",
                        index,
                        exception.Message);

                    var failures = recovery.RecordFailure(
                        actionSignature,
                        decision.Action,
                        ComputerOperatorFailureKinds.ActionRejected,
                        decision.State,
                        exception.Message,
                        decision.ExpectedEffect,
                        decision.Confidence);

                    taskHistory.Add(
                        $"STEP {index}: FAILED {actionSignature} — {exception.Message}");

                    progress.Add(
                        "recovery",
                        $"Hành động bị từ chối ({failures} lần với chiến lược này): {exception.Message}. AI sẽ quan sát lại và lập phương án khác.",
                        decision.Action,
                        decision.Confidence);

                    if (failures >= 3)
                    {
                        taskHistory.Add(
                            "REPLAN-DIRECTIVE: chiến lược này đã bị loại bỏ. Phải chọn chiến lược khác; chỉ trả blocked nếu không còn lựa chọn an toàn hợp lý.");
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
                        decision.State,
                        action.Detail,
                        decision.ExpectedEffect,
                        decision.Confidence);

                    taskHistory.Add(
                        $"STEP {index}: NOT-APPLIED {actionSignature} — {action.Detail}");

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

                ActionVerificationResult verification;
                try
                {
                    verification = await VerifyAppliedActionAsync(
                        decision,
                        frame,
                        verificationBaseline,
                        linked.Token);
                }
                finally
                {
                    verificationBaseline?.Clear();
                }

                if (!verification.Verified)
                {
                    var failures = recovery.RecordFailure(
                        actionSignature,
                        decision.Action,
                        ComputerOperatorFailureKinds.VerificationFailed,
                        decision.State,
                        verification.Detail,
                        decision.ExpectedEffect,
                        verification.Confidence);

                    taskHistory.Add(
                        $"STEP {index}: VERIFY-FAILED {actionSignature} — {verification.Detail}; EXPECTED: {decision.ExpectedEffect}");

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

                taskHistory.Add(
                    $"STEP {index}: VERIFIED {actionSignature} — {verification.Detail}; EXPECTED: {decision.ExpectedEffect}");

                if (!string.IsNullOrWhiteSpace(decision.ExpectedEffect) &&
                    verifiedMilestones.Add(decision.ExpectedEffect.Trim()))
                {
                    taskHistory.Add(
                        $"MILESTONE-SYSTEM-VERIFIED: {decision.ExpectedEffect.Trim()}");
                    progress.Add(
                        "milestone",
                        $"MỐC HỆ THỐNG ĐÃ XÁC MINH: {decision.ExpectedEffect.Trim()}",
                        "verified",
                        verification.Confidence);
                }

                loopGuard.MarkProgress();

                progress.Add(
                    "verify-result",
                    verification.Detail,
                    "verified",
                    verification.Confidence);
            }

            progress.Block(
                $"Đạt giới hạn {MaximumSteps} bước để tránh vòng lặp.");
            return Finish(
                false,
                $"Đã đạt giới hạn {MaximumSteps} bước nên dừng để tránh vòng lặp.");
        }
        catch (OperationCanceledException) when (operatorToken.IsCancellationRequested)
        {
            progress.StopByUser();
            throw new ToolExecutionStoppedByUserException(
                "Người dùng đã dừng Computer Operator từ AI Operator Console.");
        }
        catch (OperationCanceledException)
        {
            progress.Block("Computer Operator đã bị hủy.");
            throw;
        }
        catch (Exception exception)
        {
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
        string Detail);

    private async Task<ActionVerificationResult> VerifyAppliedActionAsync(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame previousFrame,
        DesktopScreenshotFrame? verificationBaseline,
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

        var after = await CapturePostActionFrameAsync(
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

            var frameDifference = verificationBaseline is null
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

            var result = await vision.VerifyAsync(
                after,
                decision.ExpectedEffect,
                frameDifference,
                cancellationToken);

            var verifiedByVision =
                result.Satisfied &&
                result.Confidence >= MinimumConfidence;

            return new(
                verifiedByVision,
                result.Confidence,
                result.Reason);
        }
        finally
        {
            after.Clear();
        }
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

    private static string BuildActionSignature(
        DesktopOperatorDecision decision)
    {
        static string Clip(string value, int length) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Length <= length
                    ? value.Trim()
                    : value.Trim()[..length];

        return decision.Action switch
        {
            "move-pointer" or
            "click-left" or
            "double-click-left" or
            "click-right" =>
                $"{decision.Action}:{CoordinateSignature(decision, false)}:{Clip(decision.TargetLabel, 80)}",
            "scroll" =>
                $"scroll:{CoordinateSignature(decision, false)}:{decision.ScrollDelta}",
            "drag-left" =>
                $"drag-left:{CoordinateSignature(decision, false)}->{CoordinateSignature(decision, true)}",
            "focus-window" => $"focus-window:{Clip(decision.Query, 80)}",
            "type-text" => $"type-text:{Clip(decision.Text, 80)}",
            "press-key" => $"press-key:{Clip(decision.Key, 20)}",
            "press-hotkey" => $"press-hotkey:{string.Join("+", decision.Keys)}",
            "open-browser" => $"open-browser:{Clip(decision.Url, 120)}",
            _ => decision.Action
        };
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


    private string BuildObservation()
    {
        var active = computer.GetActiveWindow();
        var windows = computer.GetWindows(30).Windows;

        var lines = new List<string>
        {
            active is null
                ? "Foreground: không xác định"
                : $"Foreground: id={active.WindowId}; title={active.Title}; process={active.ProcessName ?? "?"}; rect={active.Left},{active.Top},{active.Width},{active.Height}",
            "Cửa sổ đang hiển thị:"
        };

        foreach (var window in windows)
        {
            lines.Add(
                $"- id={window.WindowId}; title={window.Title}; process={window.ProcessName ?? "?"}; foreground={window.IsForeground}; rect={window.Left},{window.Top},{window.Width},{window.Height}");
        }

        return string.Join("\n", lines);
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
