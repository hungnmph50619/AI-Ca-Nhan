using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class ComputerMinimizeWindowTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = WindowSchema();

    public ToolDefinition Definition { get; } = new(
        "computer.window.minimize",
        "Thu nhỏ một cửa sổ Windows đã biết. Đây là thao tác điều khiển desktop và luôn cần xác nhận.",
        "3.1.0",
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
            computer.MinimizeWindow(
                arguments.GetProperty("windowId").GetString() ?? string.Empty)));
    }

    private static JsonElement WindowSchema()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type":"object",
              "properties":{
                "windowId":{"type":"string","pattern":"^0x[0-9A-Fa-f]+$","minLength":3,"maxLength":32}
              },
              "required":["windowId"],
              "additionalProperties":false
            }
            """);
        return document.RootElement.Clone();
    }
}

public sealed class ComputerMaximizeWindowTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseWindowSchema();

    public ToolDefinition Definition { get; } = new(
        "computer.window.maximize",
        "Phóng to một cửa sổ Windows đã biết. Luôn cần xác nhận.",
        "3.1.0",
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
            computer.MaximizeWindow(
                arguments.GetProperty("windowId").GetString() ?? string.Empty)));
    }

    private static JsonElement ParseWindowSchema()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type":"object",
              "properties":{
                "windowId":{"type":"string","pattern":"^0x[0-9A-Fa-f]+$","minLength":3,"maxLength":32}
              },
              "required":["windowId"],
              "additionalProperties":false
            }
            """);
        return document.RootElement.Clone();
    }
}

public sealed class ComputerRestoreWindowTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseWindowSchema();

    public ToolDefinition Definition { get; } = new(
        "computer.window.restore",
        "Khôi phục cửa sổ Windows đã biết từ trạng thái thu nhỏ/phóng to. Luôn cần xác nhận.",
        "3.1.0",
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
            computer.RestoreWindow(
                arguments.GetProperty("windowId").GetString() ?? string.Empty)));
    }

    private static JsonElement ParseWindowSchema()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type":"object",
              "properties":{
                "windowId":{"type":"string","pattern":"^0x[0-9A-Fa-f]+$","minLength":3,"maxLength":32}
              },
              "required":["windowId"],
              "additionalProperties":false
            }
            """);
        return document.RootElement.Clone();
    }
}

public sealed class ComputerTypeTextTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type":"object",
          "properties":{
            "windowId":{"type":"string","pattern":"^0x[0-9A-Fa-f]+$","minLength":3,"maxLength":32},
            "text":{"type":"string","minLength":1,"maxLength":1000}
          },
          "required":["windowId","text"],
          "additionalProperties":false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.keyboard.type-text",
        "Nhập văn bản hiển thị vào cửa sổ foreground đã xác nhận. Không dùng cho mật khẩu, OTP hoặc bí mật. Luôn cần xác nhận và quyền nhạy cảm.",
        "3.1.0",
        [ToolPermissions.Write, ToolPermissions.Computer, ToolPermissions.Sensitive],
        8_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(JsonSerializer.SerializeToElement(
            computer.TypeText(
                arguments.GetProperty("windowId").GetString() ?? string.Empty,
                arguments.GetProperty("text").GetString() ?? string.Empty)));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class ComputerPressKeyTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type":"object",
          "properties":{
            "windowId":{"type":"string","pattern":"^0x[0-9A-Fa-f]+$","minLength":3,"maxLength":32},
            "key":{"type":"string","minLength":1,"maxLength":20}
          },
          "required":["windowId","key"],
          "additionalProperties":false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.keyboard.press-key",
        "Nhấn một phím Windows đã hỗ trợ trong cửa sổ foreground đã xác nhận. Luôn cần xác nhận.",
        "3.1.0",
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
            computer.PressKey(
                arguments.GetProperty("windowId").GetString() ?? string.Empty,
                arguments.GetProperty("key").GetString() ?? string.Empty)));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class ComputerPressHotkeyTool(
    IComputerUseService computer) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type":"object",
          "properties":{
            "windowId":{"type":"string","pattern":"^0x[0-9A-Fa-f]+$","minLength":3,"maxLength":32},
            "keys":{
              "type":"array",
              "minItems":2,
              "maxItems":4,
              "uniqueItems":true,
              "items":{"type":"string","minLength":1,"maxLength":20}
            }
          },
          "required":["windowId","keys"],
          "additionalProperties":false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "computer.keyboard.hotkey",
        "Gửi tổ hợp 2-4 phím tới cửa sổ foreground đã xác nhận, ví dụ Ctrl+L hoặc Alt+Tab. Luôn cần xác nhận.",
        "3.1.0",
        [ToolPermissions.Write, ToolPermissions.Computer, ToolPermissions.Sensitive],
        4_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var keys = arguments
            .GetProperty("keys")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();

        return Task.FromResult(JsonSerializer.SerializeToElement(
            computer.PressHotkey(
                arguments.GetProperty("windowId").GetString() ?? string.Empty,
                keys)));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
