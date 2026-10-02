using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class ComputerVisionLocateTool(
    IDesktopScreenshotService screenshots,
    DesktopVisionService vision) : IPersonalAiTool
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

        var frame = await screenshots.CaptureStableVirtualScreenAsync(
            5000,
            cancellationToken);

        try
        {
            var result = await vision.LocateAsync(
                frame,
                target,
                cancellationToken);

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

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
