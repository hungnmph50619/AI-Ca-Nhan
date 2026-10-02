using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class ComputerFocusWindowByQueryTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type":"object",
          "properties":{
            "query":{"type":"string","minLength":2,"maxLength":120}
          },
          "required":["query"],
          "additionalProperties":false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.window.focus-by-query",
        "Tìm cửa sổ Windows đang hiển thị theo tên tiêu đề hoặc tên tiến trình rồi chuyển focus tới cửa sổ phù hợp nhất. Dùng trực tiếp cho yêu cầu như 'chuyển sang Notepad/Chrome/League' thay vì liệt kê cửa sổ trước.",
        "3.1.0",
        [ToolPermissions.Read, ToolPermissions.Write, ToolPermissions.Computer, ToolPermissions.Sensitive],
        4_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = arguments.GetProperty("query").GetString() ?? string.Empty;

        return Task.FromResult(
            JsonSerializer.SerializeToElement(
                computer.FocusWindowByQuery(query)));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
