using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class DevelopmentWorkspaceInspectTool(
    IDevelopmentAgentService development) : IPersonalAiTool
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
        "dev.workspace.inspect",
        "Quét cấu trúc source trong workspace, phát hiện project manifest và ngôn ngữ. Không chạy process nhưng metadata source được coi là nhạy cảm.",
        "1.4.0",
        [
            ToolPermissions.Read,
            ToolPermissions.Sensitive,
            ToolPermissions.Development
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
        return Task.FromResult(
            JsonSerializer.SerializeToElement(
                development.InspectWorkspace()));
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class DevelopmentTextSearchTool(
    IDevelopmentAgentService development) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "minLength": 1,
              "maxLength": 200
            },
            "caseSensitive": {
              "type": "boolean"
            },
            "maximumHits": {
              "type": "integer",
              "minimum": 1,
              "maximum": 100
            }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "dev.search.text",
        "Tìm chuỗi trong source text của workspace với giới hạn file/hit và bỏ qua build output. Không dùng grep/shell.",
        "1.4.0",
        [
            ToolPermissions.Read,
            ToolPermissions.Sensitive,
            ToolPermissions.Development
        ],
        8_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var query = arguments
            .GetProperty("query")
            .GetString()
            ?? string.Empty;
        var caseSensitive =
            arguments.TryGetProperty(
                "caseSensitive",
                out var caseElement)
            && caseElement.ValueKind == JsonValueKind.True;
        var maximumHits =
            arguments.TryGetProperty(
                "maximumHits",
                out var hitElement)
            && hitElement.TryGetInt32(out var parsedHits)
                ? parsedHits
                : 50;

        var result = await development.SearchTextAsync(
            query,
            caseSensitive,
            maximumHits,
            cancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class DevelopmentGitStatusTool(
    IDevelopmentAgentService development) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "repositoryPath": {
              "type": "string",
              "maxLength": 500
            }
          },
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "dev.git.status",
        "Chạy git status read-only trong repository nằm trong workspace. Không dùng shell và không thay đổi Git state.",
        "1.4.0",
        [
            ToolPermissions.Read,
            ToolPermissions.Sensitive,
            ToolPermissions.Development
        ],
        20_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var path = arguments.TryGetProperty(
            "repositoryPath",
            out var pathElement)
            ? pathElement.GetString() ?? "."
            : ".";

        var result = await development.GitStatusAsync(
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

public sealed class DevelopmentGitDiffTool(
    IDevelopmentAgentService development) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ParseSchema(
        """
        {
          "type": "object",
          "properties": {
            "repositoryPath": {
              "type": "string",
              "maxLength": 500
            },
            "staged": {
              "type": "boolean"
            }
          },
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "dev.git.diff",
        "Đọc git diff hoặc git diff --cached trong repository nằm trong workspace. External diff bị tắt và không có Git write action.",
        "1.4.0",
        [
            ToolPermissions.Read,
            ToolPermissions.Sensitive,
            ToolPermissions.Development
        ],
        20_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var path = arguments.TryGetProperty(
            "repositoryPath",
            out var pathElement)
            ? pathElement.GetString() ?? "."
            : ".";
        var staged =
            arguments.TryGetProperty(
                "staged",
                out var stagedElement)
            && stagedElement.ValueKind == JsonValueKind.True;

        var result = await development.GitDiffAsync(
            path,
            staged,
            cancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class DevelopmentDotnetRestoreTool(
    IDevelopmentAgentService development) : IPersonalAiTool
{
    private static readonly JsonElement Schema = TargetSchema(
        includeConfiguration: false);

    public ToolDefinition Definition { get; } = new(
        "dev.dotnet.restore",
        "Chạy dotnet restore cho một project/solution cụ thể trong workspace. Có thể truy cập package feed và thực thi MSBuild targets nên luôn cần xác nhận.",
        "1.4.0",
        [
            ToolPermissions.Read,
            ToolPermissions.Write,
            ToolPermissions.External,
            ToolPermissions.Sensitive,
            ToolPermissions.Development
        ],
        120_000,
        Schema,
        LocalOnly: false,
        RequiresConfirmation: true);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await development.DotnetRestoreAsync(
            arguments.GetProperty("targetPath").GetString()
                ?? string.Empty,
            cancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    private static JsonElement TargetSchema(bool includeConfiguration)
    {
        var json = includeConfiguration
            ? """
              {
                "type": "object",
                "properties": {
                  "targetPath": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 500
                  },
                  "configuration": {
                    "type": "string",
                    "enum": ["Debug", "Release", "debug", "release"]
                  }
                },
                "required": ["targetPath"],
                "additionalProperties": false
              }
              """
            : """
              {
                "type": "object",
                "properties": {
                  "targetPath": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 500
                  }
                },
                "required": ["targetPath"],
                "additionalProperties": false
              }
              """;

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class DevelopmentDotnetBuildTool(
    IDevelopmentAgentService development) : IPersonalAiTool
{
    private static readonly JsonElement Schema = TargetSchema();

    public ToolDefinition Definition { get; } = new(
        "dev.dotnet.build",
        "Chạy dotnet build --no-restore cho target .NET trong workspace. Project-defined MSBuild code có thể chạy, không có arbitrary CLI args.",
        "1.4.0",
        [
            ToolPermissions.Read,
            ToolPermissions.Write,
            ToolPermissions.Sensitive,
            ToolPermissions.Development
        ],
        120_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await development.DotnetBuildAsync(
            arguments.GetProperty("targetPath").GetString()
                ?? string.Empty,
            ReadConfiguration(arguments),
            cancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    private static string ReadConfiguration(JsonElement arguments) =>
        arguments.TryGetProperty(
            "configuration",
            out var element)
            ? element.GetString() ?? "Debug"
            : "Debug";

    private static JsonElement TargetSchema()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "targetPath": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 500
                },
                "configuration": {
                  "type": "string",
                  "enum": ["Debug", "Release", "debug", "release"]
                }
              },
              "required": ["targetPath"],
              "additionalProperties": false
            }
            """);
        return document.RootElement.Clone();
    }
}

public sealed class DevelopmentDotnetTestTool(
    IDevelopmentAgentService development) : IPersonalAiTool
{
    private static readonly JsonElement Schema = TargetSchema();

    public ToolDefinition Definition { get; } = new(
        "dev.dotnet.test",
        "Chạy dotnet test --no-restore cho target .NET trong workspace. Test code là code execution thực nên luôn cần xác nhận và không được retry tự động.",
        "1.4.0",
        [
            ToolPermissions.Read,
            ToolPermissions.Write,
            ToolPermissions.Sensitive,
            ToolPermissions.Development
        ],
        180_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await development.DotnetTestAsync(
            arguments.GetProperty("targetPath").GetString()
                ?? string.Empty,
            arguments.TryGetProperty(
                "configuration",
                out var configurationElement)
                ? configurationElement.GetString() ?? "Debug"
                : "Debug",
            cancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    private static JsonElement TargetSchema()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "targetPath": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 500
                },
                "configuration": {
                  "type": "string",
                  "enum": ["Debug", "Release", "debug", "release"]
                }
              },
              "required": ["targetPath"],
              "additionalProperties": false
            }
            """);
        return document.RootElement.Clone();
    }
}
