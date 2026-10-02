using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class LeaguePracticeOpenTool(
    ILeaguePracticeAutomationService automation)
    : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "league.practice.open",
        "Mở Riot/League và tự điều hướng UI tới Practice Tool bằng screenshot + vision. Chỉ thao tác trong client/menu; không điều khiển gameplay sau khi vào trận.",
        "3.0.1",
        [
            ToolPermissions.Write,
            ToolPermissions.External,
            ToolPermissions.Computer
        ],
        240_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default) =>
        JsonSerializer.SerializeToElement(
            await automation.OpenPracticeToolAsync(
                cancellationToken));

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
