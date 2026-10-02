using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class ComputerVisionClickTargetTool(
    IDesktopScreenshotService screenshots,
    DesktopVisionService vision,
    IComputerUseService computer,
    ComputerOperatorProgressStore progress,
    ComputerOperatorExecutionControl execution) : IPersonalAiTool
{
    private const double MinimumLocateConfidence = 0.78;
    private const double MinimumVerifyConfidence = 0.72;

    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type":"object",
          "properties":{
            "target":{
              "type":"string",
              "minLength":2,
              "maxLength":500
            },
            "expectedAfter":{
              "type":"string",
              "minLength":2,
              "maxLength":700
            }
          },
          "required":["target","expectedAfter"],
          "additionalProperties":false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.vision.click-target",
        "Vision-first click: chụp frame desktop ổn định, dùng Gemini Vision tìm phần tử nhìn thấy được, kiểm tra điểm click thuộc cửa sổ top-most hợp lệ, focus cửa sổ nếu cần, di chuột mượt tới tọa độ, click trái một lần, chụp frame mới và xác minh trạng thái mong đợi. Không click nếu Vision thiếu chắc chắn.",
        "3.3.0",
        [
            ToolPermissions.Write,
            ToolPermissions.External,
            ToolPermissions.Computer,
            ToolPermissions.Sensitive
        ],
        45_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        if (!vision.Ready)
            throw new ToolExecutionInputException(
                "Desktop Vision/Gemini chưa sẵn sàng.");

        var target = RequiredString(arguments, "target");
        var expectedAfter = RequiredString(arguments, "expectedAfter");

        var operatorToken = execution.Begin(
            $"Visual click: {target}");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            operatorToken);

        progress.Start($"Visual click target: {target}");

        try
        {
            progress.Add(
                "stabilize",
                "Đang chờ desktop ổn định trước khi chụp ảnh.");
            await execution.WaitIfPausedAsync(linked.Token);

            var before = await screenshots.CaptureStableVirtualScreenAsync(
                5000,
                linked.Token);

        DesktopVisionTarget located;
        try
        {
            progress.Add(
                "observe",
                $"Đã chụp frame ổn định {before.Width}x{before.Height}.",
                observation: true);
            progress.Add(
                "analyze",
                $"Vision đang tìm: {target}");

            await execution.WaitIfPausedAsync(linked.Token);
            located = await vision.LocateAsync(
                before,
                target,
                linked.Token);
        }
        finally
        {
            before.Clear();
        }

        progress.Add(
            "decide",
            located.Found
                ? $"Vision tìm thấy {located.Label}: {located.Reason}"
                : $"Vision không tìm thấy target: {located.Reason}",
            "locate",
            located.Confidence);

        if (!located.Found)
        {
            progress.Block("Không tìm thấy target trên màn hình.");
            throw new ToolExecutionInputException(
                "Vision không tìm thấy phần tử cần click trên màn hình.");
        }

        if (located.Confidence < MinimumLocateConfidence)
        {
            progress.Block(
                $"Độ tin cậy locate {located.Confidence:0.00} thấp hơn {MinimumLocateConfidence:0.00}.");
            throw new ToolExecutionInputException(
                "Vision chưa đủ chắc chắn để click phần tử này.");
        }

        var screen = computer.GetScreenInfo();
        var desktopX = screen.VirtualLeft + located.ImageX;
        var desktopY = screen.VirtualTop + located.ImageY;

        var targetWindow = computer.GetWindowAtPoint(
            desktopX,
            desktopY);
        if (targetWindow is null)
        {
            progress.Block(
                $"Không xác định được cửa sổ top-most tại ({desktopX}, {desktopY}).");
            throw new ToolExecutionInputException(
                "Tọa độ Vision không nằm trong cửa sổ top-level hợp lệ.");
        }

        var active = computer.GetActiveWindow();
        if (active is null ||
            !active.WindowId.Equals(
                targetWindow.WindowId,
                StringComparison.OrdinalIgnoreCase))
        {
            progress.Add(
                "act",
                $"Focus cửa sổ chứa target: {targetWindow.Title}",
                "focus-window",
                located.Confidence);
            await execution.WaitIfPausedAsync(linked.Token);
            computer.FocusWindow(targetWindow.WindowId);
            await Task.Delay(250, linked.Token);
        }

        progress.Add(
            "act",
            $"Di chuột tới ({desktopX}, {desktopY}).",
            "move",
            located.Confidence);
        await execution.WaitIfPausedAsync(linked.Token);
        computer.SmoothMoveCursor(
            desktopX,
            desktopY,
            420);

        progress.Add(
            "act",
            $"Click target {located.Label}.",
            "click-left",
            located.Confidence);
        await execution.WaitIfPausedAsync(linked.Token);
        computer.ClickLeft(
            targetWindow.WindowId,
            desktopX,
            desktopY);

        progress.Add(
            "verify",
            "Đang chờ giao diện phản hồi rồi chụp lại để xác minh.");

        await Task.Delay(650, linked.Token);
        await execution.WaitIfPausedAsync(linked.Token);

        progress.Add(
            "stabilize",
            "Đang chờ giao diện ổn định để chụp ảnh hậu hành động.");

        var after = await screenshots.CaptureStableVirtualScreenAsync(
            5000,
            linked.Token);

        DesktopVisionVerification verification;
        try
        {
            progress.Add(
                "observe",
                $"Đã chụp frame hậu hành động {after.Width}x{after.Height}.",
                observation: true);
            progress.Add(
                "analyze",
                $"Vision đang xác minh: {expectedAfter}");

            await execution.WaitIfPausedAsync(linked.Token);
            verification = await vision.VerifyAsync(
                after,
                expectedAfter,
                linked.Token);
        }
        finally
        {
            after.Clear();
        }

        progress.Add(
            "verify-result",
            verification.Reason,
            verification.Satisfied ? "verified" : "not-verified",
            verification.Confidence);

        var verified =
            verification.Satisfied &&
            verification.Confidence >= MinimumVerifyConfidence;

        if (verified)
            progress.Complete("Visual click đã được xác minh bằng frame mới.");
        else
            progress.Block("Click đã thực hiện nhưng trạng thái hậu hành động chưa được Vision xác minh.");

        return JsonSerializer.SerializeToElement(new
        {
            target,
            located.Label,
            locateConfidence = located.Confidence,
            desktopX,
            desktopY,
            windowId = targetWindow.WindowId,
            windowTitle = targetWindow.Title,
            expectedAfter,
            verified,
            verifyConfidence = verification.Confidence,
            verifyReason = verification.Reason
        });
        }
        catch (OperationCanceledException) when (operatorToken.IsCancellationRequested)
        {
            progress.StopByUser();
            throw new ToolExecutionStoppedByUserException(
                "Người dùng đã dừng thao tác Vision Click từ AI Operator Console.");
        }
        finally
        {
            execution.Complete();
        }
    }

    private static string RequiredString(
        JsonElement arguments,
        string property)
    {
        var value = arguments.GetProperty(property).GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new ToolExecutionInputException(
                $"Thiếu tham số {property}.");
        return value;
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
