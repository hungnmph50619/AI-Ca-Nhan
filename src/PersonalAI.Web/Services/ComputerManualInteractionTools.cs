using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class ComputerClickRightTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type":"object",
          "properties":{
            "windowId":{"type":"string","minLength":3,"maxLength":32},
            "x":{"type":"integer","minimum":-100000,"maximum":100000},
            "y":{"type":"integer","minimum":-100000,"maximum":100000}
          },
          "required":["windowId","x","y"],
          "additionalProperties":false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.mouse.click-right",
        "Nhấp chuột phải một lần tại tọa độ đã xác nhận trong cửa sổ foreground. Luôn cần xác nhận.",
        "1.2.0",
        [ToolPermissions.Write, ToolPermissions.Computer, ToolPermissions.Sensitive],
        2_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(JsonSerializer.SerializeToElement(
            computer.ClickRight(
                arguments.GetProperty("windowId").GetString() ?? string.Empty,
                arguments.GetProperty("x").GetInt32(),
                arguments.GetProperty("y").GetInt32())));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class ComputerDoubleClickLeftTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type":"object",
          "properties":{
            "windowId":{"type":"string","minLength":3,"maxLength":32},
            "x":{"type":"integer","minimum":-100000,"maximum":100000},
            "y":{"type":"integer","minimum":-100000,"maximum":100000}
          },
          "required":["windowId","x","y"],
          "additionalProperties":false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.mouse.double-click-left",
        "Nhấp đúp chuột trái tại tọa độ đã xác nhận trong cửa sổ foreground. Luôn cần xác nhận.",
        "1.2.0",
        [ToolPermissions.Write, ToolPermissions.Computer, ToolPermissions.Sensitive],
        3_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(JsonSerializer.SerializeToElement(
            computer.DoubleClickLeft(
                arguments.GetProperty("windowId").GetString() ?? string.Empty,
                arguments.GetProperty("x").GetInt32(),
                arguments.GetProperty("y").GetInt32())));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class ComputerScrollTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type":"object",
          "properties":{
            "windowId":{"type":"string","minLength":3,"maxLength":32},
            "x":{"type":"integer","minimum":-100000,"maximum":100000},
            "y":{"type":"integer","minimum":-100000,"maximum":100000},
            "delta":{"type":"integer","minimum":-2400,"maximum":2400}
          },
          "required":["windowId","x","y","delta"],
          "additionalProperties":false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.mouse.scroll",
        "Cuộn bánh xe chuột tại tọa độ đã xác nhận trong cửa sổ foreground. Luôn cần xác nhận.",
        "1.2.0",
        [ToolPermissions.Write, ToolPermissions.Computer, ToolPermissions.Sensitive],
        3_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(JsonSerializer.SerializeToElement(
            computer.Scroll(
                arguments.GetProperty("windowId").GetString() ?? string.Empty,
                arguments.GetProperty("x").GetInt32(),
                arguments.GetProperty("y").GetInt32(),
                arguments.GetProperty("delta").GetInt32())));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class ComputerDragLeftTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type":"object",
          "properties":{
            "windowId":{"type":"string","minLength":3,"maxLength":32},
            "startX":{"type":"integer","minimum":-100000,"maximum":100000},
            "startY":{"type":"integer","minimum":-100000,"maximum":100000},
            "endX":{"type":"integer","minimum":-100000,"maximum":100000},
            "endY":{"type":"integer","minimum":-100000,"maximum":100000},
            "durationMs":{"type":"integer","minimum":180,"maximum":1800}
          },
          "required":["windowId","startX","startY","endX","endY"],
          "additionalProperties":false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.mouse.drag-left",
        "Kéo-thả bằng chuột trái trong cùng cửa sổ foreground với chuyển động nhìn thấy được. Luôn cần xác nhận.",
        "1.2.0",
        [ToolPermissions.Write, ToolPermissions.Computer, ToolPermissions.Sensitive],
        5_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var duration = arguments.TryGetProperty("durationMs", out var durationValue)
            && durationValue.TryGetInt32(out var parsed)
                ? parsed
                : 500;

        return Task.FromResult(JsonSerializer.SerializeToElement(
            computer.DragLeft(
                arguments.GetProperty("windowId").GetString() ?? string.Empty,
                arguments.GetProperty("startX").GetInt32(),
                arguments.GetProperty("startY").GetInt32(),
                arguments.GetProperty("endX").GetInt32(),
                arguments.GetProperty("endY").GetInt32(),
                duration)));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
