using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class ComputerGenericAppLaunchTool(
    IComputerOperatorTaskService taskService) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type":"object",
          "properties":{
            "application":{
              "type":"string",
              "minLength":1,
              "maxLength":160
            }
          },
          "required":["application"],
          "additionalProperties":false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.app.launch",
        "Đưa một ứng dụng Windows do người dùng chỉ định vào trạng thái đang mở và hiển thị ở foreground bằng Computer Operator tổng quát. Không dùng đường dẫn .exe, không dùng shell và không có kịch bản riêng theo tên ứng dụng; AI tự quan sát desktop, tự suy luận cách mở, thao tác chuột/bàn phím, kiểm tra kết quả và đổi phương án nếu cần.",
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
        var application = NormalizeApplication(
            arguments.GetProperty("application").GetString());

        var goal =
            $"Đưa ứng dụng “{application}” vào trạng thái đang mở và hiển thị ở foreground. " +
            "Hãy tự quan sát màn hình hiện tại và tự chọn cách thực hiện phù hợp. " +
            "Nếu ứng dụng đã có cửa sổ thì có thể tận dụng cửa sổ hiện có; nếu chưa có, " +
            "hãy tự suy luận từ các thành phần Windows đang nhìn thấy và các thao tác chuột/bàn phím khả dụng. " +
            "Không giả định vị trí cố định, không giả định ứng dụng được ghim trên taskbar, " +
            "không dùng đường dẫn thực thi hoặc shell. Sau mỗi hành động phải xác minh trạng thái mới; " +
            "chỉ hoàn thành khi có bằng chứng ứng dụng đúng đã mở và đang hiển thị ở foreground.";

        var result = await taskService.RunAsync(
            goal,
            cancellationToken);

        return JsonSerializer.SerializeToElement(new
        {
            application,
            result.Completed,
            result.Summary,
            result.Steps,
            result.Provider,
            result.Model
        });
    }

    private static string NormalizeApplication(
        string? value)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length is < 1 or > 160)
            throw new ToolExecutionInputException(
                "Tên ứng dụng phải từ 1 đến 160 ký tự.");

        if (normalized.Any(character =>
                char.IsControl(character) ||
                char.IsSurrogate(character)))
            throw new ToolExecutionInputException(
                "Tên ứng dụng chứa ký tự không hợp lệ.");

        return normalized;
    }

    private static JsonElement ParseSchema(
        string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
