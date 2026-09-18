using System.Text.RegularExpressions;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IPersonalAiTool
{
    ToolDefinition Definition { get; }

    Task<System.Text.Json.JsonElement> ExecuteAsync(
        System.Text.Json.JsonElement arguments,
        CancellationToken cancellationToken = default);
}

public interface IToolRegistry
{
    IReadOnlyList<ToolDefinition> GetAll();
    bool TryGet(string name, out IPersonalAiTool? tool);
}

public sealed class ToolRegistry : IToolRegistry
{
    private static readonly Regex ValidName = new(
        @"^[a-z][a-z0-9_-]*(\.[a-z][a-z0-9_-]*)+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, IPersonalAiTool> _tools;
    private readonly IReadOnlyList<ToolDefinition> _definitions;

    public ToolRegistry(IEnumerable<IPersonalAiTool> tools)
    {
        _tools = new Dictionary<string, IPersonalAiTool>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in tools)
        {
            ValidateDefinition(tool.Definition);
            if (!_tools.TryAdd(tool.Definition.Name, tool))
            {
                throw new InvalidOperationException(
                    $"Tên công cụ bị trùng: {tool.Definition.Name}.");
            }
        }

        _definitions = _tools.Values
            .Select(tool => tool.Definition)
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<ToolDefinition> GetAll() => _definitions;

    public bool TryGet(string name, out IPersonalAiTool? tool)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            tool = null;
            return false;
        }

        return _tools.TryGetValue(name.Trim(), out tool);
    }

    private static void ValidateDefinition(ToolDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Name)
            || !ValidName.IsMatch(definition.Name))
        {
            throw new InvalidOperationException(
                $"Tên công cụ không hợp lệ: {definition.Name}.");
        }

        if (string.IsNullOrWhiteSpace(definition.Description))
        {
            throw new InvalidOperationException(
                $"Công cụ {definition.Name} phải có mô tả.");
        }

        if (definition.TimeoutMs is < 100 or > 60_000)
        {
            throw new InvalidOperationException(
                $"Thời gian chờ của công cụ {definition.Name} phải từ 100 đến 60000 mili giây.");
        }

        if (definition.InputSchema.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Lược đồ dữ liệu đầu vào của công cụ {definition.Name} phải là một đối tượng JSON.");
        }

        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var permission in definition.RequiredPermissions)
        {
            if (!ToolPermissions.All.Contains(permission))
            {
                throw new InvalidOperationException(
                    $"Công cụ {definition.Name} khai báo quyền không hợp lệ: {permission}.");
            }

            if (!permissions.Add(permission))
            {
                throw new InvalidOperationException(
                    $"Công cụ {definition.Name} khai báo quyền bị trùng: {permission}.");
            }
        }
    }
}
