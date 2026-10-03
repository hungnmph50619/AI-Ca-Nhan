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
        "Thực hiện tác vụ Windows theo mục tiêu bằng vòng lặp tự suy luận TRẠNG THÁI → PHƯƠNG ÁN → HÀNH ĐỘNG → KIỂM TRA → PHỤC HỒI → LẬP LẠI PHƯƠNG ÁN. Hệ thống ghi nhớ chiến lược thất bại và phát hiện cả trạng thái trì trệ lẫn dao động A↔B; lần đầu chỉ cảnh báo để AI đổi cách, chỉ dừng khi vòng lặp tiếp diễn sau nhiều lần replan. Click vẫn dùng bounding box và điểm an toàn, mọi hành động vẫn được xác minh. Mục tiêu mở ứng dụng được xử lý như goal tổng quát: AI tự quan sát desktop và tự chọn cửa sổ, taskbar, Start/Search hoặc bàn phím theo trạng thái thực tế; không có bảng tên ứng dụng hay đường dẫn thực thi hard-code. Tối đa 12 bước, 90 giây, Ctrl+Shift+F12 để dừng. Không shell, không xóa dữ liệu, không connector, không mật khẩu/OTP/token/bí mật.",
        "3.4.7",
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
