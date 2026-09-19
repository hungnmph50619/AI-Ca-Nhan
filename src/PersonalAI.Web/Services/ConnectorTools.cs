using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class ConnectorsListTool(
    IConnectorService connectors) : IPersonalAiTool
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
        "connectors.list",
        "Liệt kê connector đã cấu hình trong workspace hiện tại. Tên/base URL có thể nhạy cảm nên luôn cần xác nhận.",
        "1.3.0",
        [
            ToolPermissions.Read,
            ToolPermissions.Sensitive,
            ToolPermissions.Connector
        ],
        2_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = connectors.GetConnections();

        return Task.FromResult(
            JsonSerializer.SerializeToElement(
                new ConnectorListToolResult(
                    response.WorkspaceId,
                    response.Connections.Count,
                    response.Connections)));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class ConnectorHttpGetTool(
    IConnectorService connectors) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "connectorId": {
              "type": "string",
              "minLength": 36,
              "maxLength": 36
            },
            "path": {
              "type": "string",
              "minLength": 1,
              "maxLength": 1024
            }
          },
          "required": ["connectorId", "path"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "connector.http.get",
        "Đọc JSON/text từ authenticated HTTPS connector bằng Bearer token đã lưu. Chỉ GET, cùng origin, luôn cần xác nhận.",
        "1.3.0",
        [
            ToolPermissions.Read,
            ToolPermissions.External,
            ToolPermissions.Sensitive,
            ToolPermissions.Connector
        ],
        25_000,
        Schema,
        LocalOnly: false,
        RequiresConfirmation: true);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var rawId = arguments
            .GetProperty("connectorId")
            .GetString()
            ?? string.Empty;
        if (!Guid.TryParse(
            rawId,
            out var connectorId))
        {
            throw new ToolExecutionInputException(
                "Connector ID không hợp lệ.");
        }

        var path = arguments
            .GetProperty("path")
            .GetString()
            ?? string.Empty;

        var result = await connectors.ReadAsync(
            connectorId,
            path,
            cancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
