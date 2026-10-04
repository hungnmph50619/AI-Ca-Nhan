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
        "Thực hiện tác vụ Windows nhiều bước bằng vòng lặp tự suy luận TRẠNG THÁI → MỤC TIÊU CON → PHƯƠNG ÁN → HÀNH ĐỘNG → KIỂM TRA → PHỤC HỒI → LẬP LẠI PHƯƠNG ÁN. AI theo dõi tiến độ và mốc đã xác minh, đồng thời có thể chụp riêng cửa sổ foreground để giảm nhiễu khi xác minh thao tác nội bộ ứng dụng; frame mang metadata phạm vi, windowId, tiêu đề và trạng thái foreground; v3.5.5 bổ sung frame differencing trước/sau hành động; v3.5.6 bổ sung foreground/occlusion awareness; v3.5.7 bổ sung temporal scene consistency; v3.6.0 tách generic action executor; v3.6.1 revalidate cửa sổ topmost ngay trước click/scroll/drag; v3.6.2 bổ sung desktop-intent routing; v3.6.3 bổ sung Canonical Interaction Space; v3.6.4 bổ sung bounded blocked-replan và console cuộn được; v3.6.5 làm scene graph tolerant; v3.6.6 tách phím điều khiển khỏi type-text và Việt hóa Operator Console; v3.6.7 luôn bám dòng log mới nhất và đồng bộ timeout; v3.6.8 bổ sung Hybrid Verification: local xác minh nhanh foreground/frame-change để bỏ lượt Gemini Verify không cần thiết; Gemini vẫn chịu trách nhiệm suy luận/xác minh ngữ nghĩa khi local chưa đủ chắc chắn; các lỗi Vision tạm thời 429/500/502/503/504 được tự thử lại có backoff thay vì làm hỏng tác vụ ngay. Vẫn giữ mở ứng dụng tổng quát, bounding box/điểm click an toàn, recovery memory và loop guard. Tối đa 12 bước, 180 giây, Ctrl+Shift+F12 để dừng. Không shell, không xóa dữ liệu, không connector, không mật khẩu/OTP/token/bí mật.",
        "3.6.8",
        [
            ToolPermissions.Write,
            ToolPermissions.External,
            ToolPermissions.Computer,
            ToolPermissions.Sensitive
        ],
        180_000,
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
