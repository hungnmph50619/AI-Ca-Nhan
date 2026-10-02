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
    ILogger<ComputerOperatorTaskService> logger)
    : IComputerOperatorTaskService
{
    private const int MaximumSteps = 8;
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
                        linked.Token);
                }
                finally
                {
                    frame.Clear();
                }

                progress.Add(
                    "decide",
                    $"Vision: {decision.Reason}",
                    decision.Action,
                    decision.Confidence);

                if (decision.Action == "complete")
                {
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
                    progress.Block(
                        $"Vision dừng an toàn: {decision.Reason}");
                    return Finish(
                        false,
                        $"Vision dừng an toàn: {decision.Reason}");
                }

                if (decision.Action == "wait")
                {
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

                progress.Add(
                    "act",
                    $"Chuẩn bị thực hiện: {decision.Action}. {decision.Reason}",
                    decision.Action,
                    decision.Confidence);

                ComputerActionResponse action;
                try
                {
                    action = ExecuteDecision(decision);
                }
                catch (ToolExecutionInputException exception)
                {
                    logger.LogWarning(
                        "Computer Operator step {Step} rejected: {Reason}",
                        index,
                        exception.Message);
                    progress.Block(
                        $"Bước {index} bị từ chối: {exception.Message}");
                    return Finish(
                        false,
                        $"Dừng ở bước {index}: {exception.Message}");
                }

                steps.Add(new ComputerOperatorTaskStep(
                    index,
                    decision.Action,
                    action.Detail));

                progress.Add(
                    "acted",
                    action.Detail,
                    decision.Action,
                    decision.Confidence,
                    actionTaken: true);

                progress.Add(
                    "verify",
                    "Đang chờ giao diện phản hồi; vòng tiếp theo sẽ chụp màn hình mới để xác minh kết quả.");

                await Task.Delay(700, linked.Token);
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

    private ComputerActionResponse ExecuteDecision(
        DesktopOperatorDecision decision)
    {
        var active = computer.GetActiveWindow();

        return decision.Action switch
        {
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
