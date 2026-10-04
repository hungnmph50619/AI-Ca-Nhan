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
        "Thực hiện tác vụ Windows nhiều bước bằng vòng lặp tự suy luận TRẠNG THÁI → MỤC TIÊU CON → PHƯƠNG ÁN → HÀNH ĐỘNG → KIỂM TRA → PHỤC HỒI → LẬP LẠI PHƯƠNG ÁN. AI theo dõi tiến độ và mốc đã xác minh, đồng thời có thể chụp riêng cửa sổ foreground để giảm nhiễu khi xác minh thao tác nội bộ ứng dụng; frame mang metadata phạm vi, windowId, tiêu đề và trạng thái foreground; v3.5.5 bổ sung frame differencing trước/sau hành động; v3.5.6 bổ sung foreground/occlusion awareness; v3.5.7 bổ sung temporal scene consistency; v3.6.0 tách generic action executor; v3.6.1 revalidate cửa sổ topmost ngay trước click/scroll/drag để tránh race condition khi UI đổi trong lúc di chuột. Vẫn giữ mở ứng dụng tổng quát, bounding box/điểm click an toàn, recovery memory và loop guard. Tối đa 12 bước, 90 giây, Ctrl+Shift+F12 để dừng. Không shell, không xóa dữ liệu, không connector, không mật khẩu/OTP/token/bí mật.",
        "3.6.1",
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
