using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class BrowserSessionInfoTool(
    IBrowserAgentService browser) : IPersonalAiTool
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
        "browser.session.info",
        "Đọc trạng thái Browser Agent trong workspace hiện tại: URL, origin, tiêu đề và metadata snapshot. Không tạo network request.",
        "1.2.0",
        [ToolPermissions.Read, ToolPermissions.Browser],
        2_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            JsonSerializer.SerializeToElement(
                browser.GetSessionInfo()));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class BrowserNavigateTool(
    IBrowserAgentService browser) : IPersonalAiTool
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
          "required": ["url"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "browser.navigate",
        "Điều hướng Browser Agent bằng HTTP GET tới URL công khai đã qua SSRF/origin guard. Tạo network request ra bên ngoài và luôn cần xác nhận.",
        "1.2.0",
        [ToolPermissions.Read, ToolPermissions.External, ToolPermissions.Browser],
        25_000,
        Schema,
        LocalOnly: false,
        RequiresConfirmation: true);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var url = arguments
            .GetProperty("url")
            .GetString()
            ?? string.Empty;

        var result = await browser.NavigateAsync(
            url,
            cancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class BrowserPageObserveTool(
    IBrowserAgentService browser) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "maximumCharacters": {
              "type": "integer",
              "minimum": 1,
              "maximum": 24000
            },
            "maximumLinks": {
              "type": "integer",
              "minimum": 0,
              "maximum": 100
            }
          },
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "browser.page.observe",
        "Đọc snapshot text và liên kết của trang Browser Agent hiện tại. Không tạo network request mới nhưng cần xác nhận Browser permission.",
        "1.2.0",
        [ToolPermissions.Read, ToolPermissions.Browser],
        3_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var maximumCharacters =
            arguments.TryGetProperty(
                "maximumCharacters",
                out var charactersElement)
            && charactersElement.TryGetInt32(out var parsedCharacters)
                ? parsedCharacters
                : BrowserAgentService.MaximumPageTextCharacters;

        var maximumLinks =
            arguments.TryGetProperty(
                "maximumLinks",
                out var linksElement)
            && linksElement.TryGetInt32(out var parsedLinks)
                ? parsedLinks
                : BrowserAgentService.MaximumLinks;

        return Task.FromResult(
            JsonSerializer.SerializeToElement(
                browser.ObservePage(
                    maximumCharacters,
                    maximumLinks)));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class BrowserLinkOpenTool(
    IBrowserAgentService browser) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "linkIndex": {
              "type": "integer",
              "minimum": 1,
              "maximum": 100
            }
          },
          "required": ["linkIndex"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "browser.link.open",
        "Mở một liên kết theo index từ snapshot hiện tại bằng HTTP GET sau khi kiểm tra URL/DNS. Tạo network request ra bên ngoài và luôn cần xác nhận.",
        "1.2.0",
        [ToolPermissions.Read, ToolPermissions.External, ToolPermissions.Browser],
        25_000,
        Schema,
        LocalOnly: false,
        RequiresConfirmation: true);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var linkIndex = arguments
            .GetProperty("linkIndex")
            .GetInt32();

        var result = await browser.OpenLinkAsync(
            linkIndex,
            cancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
