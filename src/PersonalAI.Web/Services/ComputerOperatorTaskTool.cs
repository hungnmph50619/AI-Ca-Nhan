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
        "Thực hiện tác vụ Windows theo mục tiêu bằng vòng lặp tự suy luận TRẠNG THÁI → PHƯƠNG ÁN → QUYẾT ĐỊNH → HÀNH ĐỘNG → KIỂM TRA → LẬP LẠI PHƯƠNG ÁN. Với hành động click, AI phải trả vùng bao quanh mục tiêu; hệ thống tự chọn điểm an toàn bên trong vùng trước khi di chuột/click. Hỗ trợ nhiều hệ tọa độ, chuột, cửa sổ và bàn phím; hành động thất bại được ghi vào lịch sử để tự đổi chiến lược. Tối đa 12 bước, 90 giây, có chống vòng lặp và Ctrl+Shift+F12 để dừng. Không shell, không xóa dữ liệu, không connector, không mật khẩu/OTP/token/bí mật.",
        "3.4.4",
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
