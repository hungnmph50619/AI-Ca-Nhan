using System.Text.RegularExpressions;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ToolExposureSelection(
    IReadOnlyList<ToolDefinition> Tools,
    int ConsideredTools,
    int SelectedTools,
    IReadOnlyList<string> IntentCapabilities,
    string Reason);

public interface IToolExposurePolicy
{
    ToolExposureSelection Select(
        string intent,
        IReadOnlyList<ToolDefinition> definitions,
        int maximumTools = 16);
}

/// <summary>
/// Giới hạn tool provider được nhìn thấy theo intent/capability tổng quát.
/// Không hard-code ứng dụng cụ thể và không thay đổi registry hay quyền execute.
/// </summary>
public sealed class ToolExposurePolicy : IToolExposurePolicy
{
    private static readonly Regex TokenSeparator =
        new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, string[]> CapabilitySignals =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["computer"] =
            [
                "computer", "desktop", "screen", "window", "mouse", "keyboard",
                "click", "type", "focus", "màn", "hình", "chuột", "bàn", "phím",
                "nhấp", "cửa", "sổ", "ứng", "dụng"
            ],
            ["browser"] =
            [
                "browser", "web", "website", "url", "http", "page",
                "trang", "web", "duyệt"
            ],
            ["coding"] =
            [
                "code", "coding", "repo", "repository", "git", "build", "compile",
                "test", "project", "source", "mã", "nguồn", "lập", "trình"
            ],
            ["connector"] =
            [
                "connector", "email", "mail", "inbox", "calendar", "drive",
                "slack", "account", "hộp", "thư", "lịch", "tài", "khoản"
            ],
            ["workspace"] =
            [
                "file", "folder", "document", "text", "read", "write", "save",
                "delete", "move", "directory", "tệp", "thư", "mục", "văn", "bản",
                "đọc", "ghi", "lưu", "xóa", "di", "chuyển"
            ]
        };

    public ToolExposureSelection Select(
        string intent,
        IReadOnlyList<ToolDefinition> definitions,
        int maximumTools = 16)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        maximumTools = Math.Clamp(maximumTools, 4, 32);
        var normalizedIntent = Normalize(intent);
        var tokens = Tokenize(normalizedIntent);
        var requested = DetectCapabilities(tokens);

        var ranked = definitions
            .Select(definition => new
            {
                Definition = definition,
                Score = Score(definition, tokens, requested)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => RiskRank(item.Definition))
            .ThenBy(item => item.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maximumTools)
            .Select(item => item.Definition)
            .ToArray();

        return new(
            ranked,
            definitions.Count,
            ranked.Length,
            requested,
            $"intentCapabilities=[{string.Join(",", requested)}]; considered={definitions.Count}; selected={ranked.Length}; max={maximumTools}; policy=capability-ranked");
    }

    private static int Score(
        ToolDefinition definition,
        IReadOnlySet<string> intentTokens,
        IReadOnlyList<string> requested)
    {
        var score = 0;
        var capabilities = GetCapabilities(definition);

        foreach (var capability in requested)
        {
            if (capabilities.Contains(capability))
                score += 50;
        }

        var searchable = Tokenize(
            Normalize(
                $"{definition.Name} {definition.Description} {string.Join(" ", capabilities)}"));

        score += searchable.Count(token => intentTokens.Contains(token)) * 4;

        var root = definition.Name.Split('.', 2)[0].ToLowerInvariant();
        if (requested.Contains(root, StringComparer.OrdinalIgnoreCase))
            score += 20;

        if (definition.RequiredPermissions.Count == 0 ||
            definition.RequiredPermissions.All(permission =>
                permission.Equals(ToolPermissions.Read, StringComparison.OrdinalIgnoreCase)))
        {
            score += 6;
        }

        if (definition.Name.Equals("computer.operator.run-task", StringComparison.OrdinalIgnoreCase) &&
            requested.Contains("computer", StringComparer.OrdinalIgnoreCase))
        {
            score += 30;
        }

        if (definition.RequiredPermissions.Any(permission =>
                permission.Equals(ToolPermissions.Delete, StringComparison.OrdinalIgnoreCase) ||
                permission.Equals(ToolPermissions.Sensitive, StringComparison.OrdinalIgnoreCase)))
        {
            score -= 8;
        }

        return score;
    }

    private static IReadOnlyList<string> DetectCapabilities(
        IReadOnlySet<string> tokens)
    {
        var result = new List<string>();

        foreach (var pair in CapabilitySignals)
        {
            if (pair.Value.Any(tokens.Contains))
                result.Add(pair.Key);
        }

        return result;
    }

    private static HashSet<string> GetCapabilities(
        ToolDefinition definition)
    {
        var result = new HashSet<string>(
            definition.Capabilities ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        var name = definition.Name;

        if (name.StartsWith("computer.", StringComparison.OrdinalIgnoreCase) ||
            definition.RequiredPermissions.Contains(
                ToolPermissions.Computer,
                StringComparer.OrdinalIgnoreCase))
            result.Add("computer");

        if (name.StartsWith("browser.", StringComparison.OrdinalIgnoreCase) ||
            definition.RequiredPermissions.Contains(
                ToolPermissions.Browser,
                StringComparer.OrdinalIgnoreCase))
            result.Add("browser");

        if (name.StartsWith("development.", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("developer.", StringComparison.OrdinalIgnoreCase) ||
            definition.RequiredPermissions.Contains(
                ToolPermissions.Development,
                StringComparer.OrdinalIgnoreCase))
            result.Add("coding");

        if (name.StartsWith("connector.", StringComparison.OrdinalIgnoreCase) ||
            definition.RequiredPermissions.Contains(
                ToolPermissions.Connector,
                StringComparer.OrdinalIgnoreCase))
            result.Add("connector");

        if (name.StartsWith("workspace.", StringComparison.OrdinalIgnoreCase))
            result.Add("workspace");

        if (result.Count == 0)
            result.Add("general");

        return result;
    }

    private static int RiskRank(
        ToolDefinition definition)
    {
        if (definition.RequiredPermissions.Any(permission =>
                permission.Equals(ToolPermissions.Delete, StringComparison.OrdinalIgnoreCase) ||
                permission.Equals(ToolPermissions.Sensitive, StringComparison.OrdinalIgnoreCase) ||
                permission.Equals(ToolPermissions.External, StringComparison.OrdinalIgnoreCase)))
            return 2;

        if (definition.RequiresConfirmation ||
            definition.RequiredPermissions.Any(ToolPermissions.RequiresExplicitConfirmation))
            return 1;

        return 0;
    }

    private static HashSet<string> Tokenize(string value) =>
        TokenSeparator.Split(value)
            .Where(token => token.Length >= 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();
}
