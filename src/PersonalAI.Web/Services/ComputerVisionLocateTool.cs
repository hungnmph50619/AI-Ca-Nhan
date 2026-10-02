using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class ComputerVisionLocateTool(
    IDesktopScreenshotService screenshots,
    DesktopVisionService vision,
    ComputerOperatorProgressStore progress,
    ComputerOperatorExecutionControl execution) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type":"object",
          "properties":{
            "target":{
              "type":"string",
              "minLength":2,
              "maxLength":500
            }
          },
          "required":["target"],
          "additionalProperties":false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.vision.locate",
        "Chụp một frame desktop ổn định rồi gửi ảnh tới Gemini Desktop Vision để tìm phần tử giao diện được mô tả. Chỉ quan sát và trả tọa độ; không di chuột, không click và không gõ phím.",
        "3.2.0",
        [
            ToolPermissions.Read,
            ToolPermissions.External,
            ToolPermissions.Computer,
            ToolPermissions.Sensitive
        ],
        30_000,
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

        var target = arguments
            .GetProperty("target")
            .GetString()
            ?.Trim();

        if (string.IsNullOrWhiteSpace(target))
            throw new ToolExecutionInputException(
                "Mô tả phần tử cần tìm đang trống.");

        var operatorToken = execution.Begin(
            $"Tìm phần tử trên màn hình: {target}");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            operatorToken);

        progress.Start(
            $"Tìm phần tử trên màn hình: {target}");

        try
        {
            progress.Add(
                "stabilize",
                "Đang chờ desktop ổn định trước khi chụp ảnh.");
            await execution.WaitIfPausedAsync(linked.Token);

            var frame = await screenshots.CaptureStableVirtualScreenAsync(
                5000,
                linked.Token);

            try
            {
                progress.Add(
                    "screenshot",
                    $"Đã chụp desktop {frame.Width}x{frame.Height}.",
                    observation: true);

                await execution.WaitIfPausedAsync(linked.Token);
                progress.Add(
                    "vision",
                    $"Đang gửi ảnh tới Gemini Desktop Vision để xác định: {target}");

                var result = await vision.LocateAsync(
                    frame,
                    target,
                    linked.Token);

                progress.Add(
                    "locate",
                    result.Found
                        ? $"Đã tìm thấy {result.Label} tại ({frame.Left + result.ImageX}, {frame.Top + result.ImageY}). {result.Reason}"
                        : $"Không tìm thấy phần tử. {result.Reason}",
                    "locate",
                    result.Confidence);

                if (result.Found)
                    progress.Complete(
                        $"Đã xác định vị trí {result.Label}, chưa thực hiện click.");
                else
                    progress.Block(
                        "Vision không tìm thấy phần tử được yêu cầu trên frame hiện tại.");

                return JsonSerializer.SerializeToElement(new
                {
                    result.Found,
                    result.Label,
                    result.Confidence,
                    result.Reason,
                    imageX = result.ImageX,
                    imageY = result.ImageY,
                    desktopX = frame.Left + result.ImageX,
                    desktopY = frame.Top + result.ImageY,
                    frame.Left,
                    frame.Top,
                    frame.Width,
                    frame.Height,
                    frame.CapturedAtUtc
                });
            }
            finally
            {
                frame.Clear();
            }
        }
        catch (OperationCanceledException) when (operatorToken.IsCancellationRequested)
        {
            progress.StopByUser();
            throw new ToolExecutionStoppedByUserException(
                "Người dùng đã dừng Desktop Vision từ AI Operator Console.");
        }
        finally
        {
            execution.Complete();
        }
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
