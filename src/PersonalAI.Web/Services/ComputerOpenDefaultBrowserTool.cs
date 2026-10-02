using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

/// <summary>
/// Mở trình duyệt mặc định của Windows tới một URL HTTP/HTTPS đã kiểm tra.
/// Không nhận executable, command line, file path hoặc URI scheme tùy ý.
/// </summary>
public sealed class ComputerOpenDefaultBrowserTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "url": {
              "type": "string",
              "minLength": 8,
              "maxLength": 2048
            }
          },
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.browser.open-default",
        "Mở trình duyệt mặc định của Windows. Nếu có URL thì chỉ chấp nhận HTTP/HTTPS; không chạy executable, shell command hoặc protocol tùy ý.",
        "3.0.1",
        [
            ToolPermissions.Write,
            ToolPermissions.External,
            ToolPermissions.Computer
        ],
        5_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var url = arguments.TryGetProperty("url", out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        return Task.FromResult(
            JsonSerializer.SerializeToElement(
                computer.OpenDefaultBrowser(url)));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
