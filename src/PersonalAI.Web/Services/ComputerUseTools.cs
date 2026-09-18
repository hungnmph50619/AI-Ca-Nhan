using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class ComputerScreenInfoTool(
    IComputerUseService computer) : IPersonalAiTool
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
        "computer.screen.info",
        "Đọc kích thước màn hình chính, desktop ảo và số màn hình trên Windows mà không thay đổi trạng thái máy.",
        "1.1.0",
        [ToolPermissions.Read],
        2_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: false);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            JsonSerializer.SerializeToElement(
                computer.GetScreenInfo()));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class ComputerCursorPositionTool(
    IComputerUseService computer) : IPersonalAiTool
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
        "computer.cursor.position",
        "Đọc tọa độ con trỏ hiện tại trên desktop Windows mà không di chuyển con trỏ.",
        "1.1.0",
        [ToolPermissions.Read],
        2_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: false);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            JsonSerializer.SerializeToElement(
                computer.GetCursorPosition()));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class ComputerWindowsListTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "limit": {
              "type": "integer",
              "minimum": 1,
              "maximum": 50
            }
          },
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.windows.list",
        "Liệt kê cửa sổ desktop đang hiển thị. Tiêu đề cửa sổ có thể chứa dữ liệu nhạy cảm nên cần xác nhận.",
        "1.1.0",
        [ToolPermissions.Read, ToolPermissions.Sensitive],
        3_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var limit = arguments.TryGetProperty(
            "limit",
            out var value)
            && value.TryGetInt32(out var parsed)
                ? parsed
                : 30;

        return Task.FromResult(
            JsonSerializer.SerializeToElement(
                computer.GetWindows(limit)));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class ComputerActiveWindowTool(
    IComputerUseService computer) : IPersonalAiTool
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
        "computer.window.active",
        "Đọc cửa sổ foreground hiện tại trên Windows. Tiêu đề cửa sổ có thể chứa dữ liệu nhạy cảm nên cần xác nhận.",
        "1.1.0",
        [ToolPermissions.Read, ToolPermissions.Sensitive],
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
            JsonSerializer.SerializeToElement(new
            {
                window = computer.GetActiveWindow()
            }));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class ComputerFocusWindowTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "windowId": {
              "type": "string",
              "minLength": 3,
              "maxLength": 32
            }
          },
          "required": ["windowId"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.window.focus",
        "Chuyển focus tới một cửa sổ desktop Windows đã biết. Đây là thao tác điều khiển máy và luôn cần xác nhận.",
        "1.1.0",
        [
            ToolPermissions.Write,
            ToolPermissions.Computer,
            ToolPermissions.Sensitive
        ],
        3_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var windowId = arguments
            .GetProperty("windowId")
            .GetString()
            ?? string.Empty;

        return Task.FromResult(
            JsonSerializer.SerializeToElement(
                computer.FocusWindow(windowId)));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class ComputerMoveCursorTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "x": {
              "type": "integer",
              "minimum": -100000,
              "maximum": 100000
            },
            "y": {
              "type": "integer",
              "minimum": -100000,
              "maximum": 100000
            }
          },
          "required": ["x", "y"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.cursor.move",
        "Di chuyển con trỏ chuột tới một tọa độ trong desktop ảo Windows. Không click. Luôn cần xác nhận.",
        "1.1.0",
        [ToolPermissions.Write, ToolPermissions.Computer],
        2_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var x = arguments.GetProperty("x").GetInt32();
        var y = arguments.GetProperty("y").GetInt32();

        return Task.FromResult(
            JsonSerializer.SerializeToElement(
                computer.MoveCursor(x, y)));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
