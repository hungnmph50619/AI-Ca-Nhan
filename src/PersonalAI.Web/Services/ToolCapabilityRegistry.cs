using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class ToolRiskLevels
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
}

public sealed record UnifiedToolCapability(
    string Name,
    string Description,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Permissions,
    string RiskLevel,
    bool RequiresConfirmation,
    bool SupportsVerification,
    bool SupportsRollback,
    bool LocalOnly);

public interface IToolCapabilityRegistry
{
    IReadOnlyList<UnifiedToolCapability> GetAll();

    bool TryGet(
        string name,
        out UnifiedToolCapability? capability);

    IReadOnlyList<UnifiedToolCapability> FindByCapability(
        string capability);
}

public sealed class ToolCapabilityRegistry(
    IToolRegistry tools)
    : IToolCapabilityRegistry
{
    private readonly IReadOnlyList<UnifiedToolCapability> _capabilities =
        tools.GetAll()
            .Select(Project)
            .OrderBy(
                item => item.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<UnifiedToolCapability> GetAll() =>
        _capabilities;

    public bool TryGet(
        string name,
        out UnifiedToolCapability? capability)
    {
        capability = _capabilities.FirstOrDefault(item =>
            item.Name.Equals(
                (name ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase));

        return capability is not null;
    }

    public IReadOnlyList<UnifiedToolCapability> FindByCapability(
        string capability)
    {
        var normalized = (capability ?? string.Empty).Trim();

        if (normalized.Length == 0)
            return Array.Empty<UnifiedToolCapability>();

        return _capabilities
            .Where(item =>
                item.Capabilities.Any(value =>
                    value.Equals(
                        normalized,
                        StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static UnifiedToolCapability Project(
        ToolDefinition definition)
    {
        var capabilities =
            definition.Capabilities is { Count: > 0 }
                ? definition.Capabilities
                    .Where(value =>
                        !string.IsNullOrWhiteSpace(value))
                    .Select(value =>
                        value.Trim().ToLowerInvariant())
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : InferCapabilities(definition);

        var risk = string.IsNullOrWhiteSpace(
                definition.RiskLevel)
            ? InferRisk(definition)
            : definition.RiskLevel!
                .Trim()
                .ToLowerInvariant();

        return new(
            definition.Name,
            definition.Description,
            capabilities,
            definition.RequiredPermissions,
            risk,
            definition.RequiresConfirmation,
            definition.SupportsVerification ||
            definition.Name.Equals(
                "computer.operator.run-task",
                StringComparison.OrdinalIgnoreCase),
            definition.SupportsRollback,
            definition.LocalOnly);
    }

    private static IReadOnlyList<string> InferCapabilities(
        ToolDefinition definition)
    {
        var result = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var name = definition.Name;

        if (name.StartsWith(
                "computer.",
                StringComparison.OrdinalIgnoreCase))
        {
            result.Add("computer");
        }

        if (name.StartsWith(
                "browser.",
                StringComparison.OrdinalIgnoreCase) ||
            definition.RequiredPermissions.Contains(
                ToolPermissions.Browser,
                StringComparer.OrdinalIgnoreCase))
        {
            result.Add("browser");
        }

        if (name.StartsWith(
                "development.",
                StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(
                "developer.",
                StringComparison.OrdinalIgnoreCase) ||
            definition.RequiredPermissions.Contains(
                ToolPermissions.Development,
                StringComparer.OrdinalIgnoreCase))
        {
            result.Add("coding");
        }

        if (name.StartsWith(
                "connector.",
                StringComparison.OrdinalIgnoreCase) ||
            definition.RequiredPermissions.Contains(
                ToolPermissions.Connector,
                StringComparer.OrdinalIgnoreCase))
        {
            result.Add("connector");
        }

        if (definition.RequiredPermissions.Contains(
                ToolPermissions.Read,
                StringComparer.OrdinalIgnoreCase))
        {
            result.Add("read");
        }

        if (definition.RequiredPermissions.Any(permission =>
                permission.Equals(
                    ToolPermissions.Write,
                    StringComparison.OrdinalIgnoreCase) ||
                permission.Equals(
                    ToolPermissions.Delete,
                    StringComparison.OrdinalIgnoreCase) ||
                permission.Equals(
                    ToolPermissions.External,
                    StringComparison.OrdinalIgnoreCase)))
        {
            result.Add("side-effect");
        }

        if (result.Count == 0)
            result.Add("general");

        return result
            .OrderBy(
                value => value,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string InferRisk(
        ToolDefinition definition)
    {
        if (definition.RequiredPermissions.Any(permission =>
                permission.Equals(
                    ToolPermissions.Delete,
                    StringComparison.OrdinalIgnoreCase) ||
                permission.Equals(
                    ToolPermissions.Sensitive,
                    StringComparison.OrdinalIgnoreCase) ||
                permission.Equals(
                    ToolPermissions.External,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return ToolRiskLevels.High;
        }

        if (definition.RequiresConfirmation ||
            definition.RequiredPermissions.Any(permission =>
                permission.Equals(
                    ToolPermissions.Write,
                    StringComparison.OrdinalIgnoreCase) ||
                permission.Equals(
                    ToolPermissions.Computer,
                    StringComparison.OrdinalIgnoreCase) ||
                permission.Equals(
                    ToolPermissions.Browser,
                    StringComparison.OrdinalIgnoreCase) ||
                permission.Equals(
                    ToolPermissions.Development,
                    StringComparison.OrdinalIgnoreCase) ||
                permission.Equals(
                    ToolPermissions.Connector,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return ToolRiskLevels.Medium;
        }

        return ToolRiskLevels.Low;
    }
}
