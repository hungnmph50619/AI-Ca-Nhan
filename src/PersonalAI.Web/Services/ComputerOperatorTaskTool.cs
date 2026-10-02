using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class ComputerOperatorTaskTool(
    IComputerOperatorTaskService taskService) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type":"object",
          "properties":{
            "goal":{
              "type":"string",
              "minLength":2,
              "maxLength":1200
            }
          },
          "required":["goal"],
          "additionalProperties":false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.operator.run-task",
        "Thực hiện một tác vụ Windows nhiều bước với vòng lặp OBSERVE → ANALYZE → DECIDE → ACT → VERIFY và một lần xác nhận ở cấp tác vụ. Mỗi vòng chụp desktop hiện tại và gửi ảnh tới Gemini Desktop Vision để quyết định bước kế tiếp; overlay/terminal hiển thị tiến trình. Có thể tìm/chuyển/thu nhỏ/phóng to/khôi phục cửa sổ, nhập văn bản không nhạy cảm, nhấn phím/hotkey và mở trình duyệt HTTP/HTTPS. Tối đa 8 bước, 90 giây, Ctrl+Shift+F12 để dừng. Không shell, không xóa dữ liệu, không connector, không mật khẩu/OTP/token/bí mật, chưa tự click tọa độ.",
        "3.2.0",
        [
            ToolPermissions.Write,
            ToolPermissions.External,
            ToolPermissions.Computer,
            ToolPermissions.Sensitive
        ],
        100_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await taskService.RunAsync(
            arguments.GetProperty("goal").GetString() ?? string.Empty,
            cancellationToken);

        return JsonSerializer.SerializeToElement(result);
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
