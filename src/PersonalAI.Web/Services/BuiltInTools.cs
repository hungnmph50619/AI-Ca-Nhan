using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class TextStatsTool : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "text": {
              "type": "string",
              "minLength": 1,
              "maxLength": 12000
            }
          },
          "required": ["text"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "local.text_stats",
        "Đếm ký tự, từ, dòng và số byte UTF-8 của một đoạn văn bản ngay trên máy.",
        "1.0.0",
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
        var text = arguments.GetProperty("text").GetString() ?? string.Empty;
        var wordCount = Regex.Matches(
            text.Normalize(NormalizationForm.FormKC),
            @"[\p{L}\p{N}]+",
            RegexOptions.CultureInvariant).Count;
        var lineCount = text.Count(character => character == '\n') + 1;
        var utf8Bytes = Encoding.UTF8.GetByteCount(text);

        return Task.FromResult(JsonSerializer.SerializeToElement(new
        {
            characterCount = text.Length,
            wordCount,
            lineCount,
            utf8Bytes
        }));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class LocalClockTool : IPersonalAiTool
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
        "local.clock",
        "Đọc thời gian UTC, thời gian máy chủ cục bộ và múi giờ hệ thống mà không gọi dịch vụ bên ngoài.",
        "1.0.0",
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
        var utcNow = DateTimeOffset.UtcNow;
        var localNow = TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.Local);

        return Task.FromResult(JsonSerializer.SerializeToElement(new
        {
            utcNow,
            localNow,
            timeZoneId = TimeZoneInfo.Local.Id,
            utcOffsetMinutes = checked((int)localNow.Offset.TotalMinutes)
        }));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
