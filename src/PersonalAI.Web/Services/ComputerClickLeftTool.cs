using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

/// <summary>
/// Một lần nhấp trái thủ công. Không dùng trong vòng lặp tác nhân và không nhấp
/// nếu cửa sổ đích không còn là cửa sổ đang hoạt động.
/// </summary>
public sealed class ComputerClickLeftTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "windowId": {
              "type": "string", "minLength": 3, "maxLength": 32
            },
            "x": { "type": "integer", "minimum": -100000, "maximum": 100000 },
            "y": { "type": "integer", "minimum": -100000, "maximum": 100000 }
          },
          "required": ["windowId", "x", "y"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.mouse.click-left",
        "Gửi đúng một lần nhấp chuột trái tới cửa sổ đang hoạt động và tọa độ đã chọn. Có thể kích hoạt thao tác trong ứng dụng đích. Luôn cần xác nhận thủ công, quyền ĐIỀU KHIỂN MÁY và quyền NHẠY CẢM; không tự chạy liên tục.",
        "1.1.3-controlled-preview",
        [ToolPermissions.Write, ToolPermissions.Computer, ToolPermissions.Sensitive],
        2_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var windowId = arguments.GetProperty("windowId").GetString()
            ?? string.Empty;
        var x = arguments.GetProperty("x").GetInt32();
        var y = arguments.GetProperty("y").GetInt32();
        return Task.FromResult(JsonSerializer.SerializeToElement(
            computer.ClickLeft(windowId, x, y)));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
