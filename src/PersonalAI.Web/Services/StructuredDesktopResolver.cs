namespace PersonalAI.Web.Services;

public enum StructuredResolutionStatus
{
    Resolved,
    NotFound,
    Ambiguous
}

public sealed record StructuredResolutionCandidate(
    UnifiedStructuredSceneNode Node,
    int Score);

public sealed record StructuredResolutionResult(
    StructuredResolutionStatus Status,
    UnifiedStructuredSceneNode? Node,
    int Score,
    IReadOnlyList<StructuredResolutionCandidate> Candidates,
    string Reason)
{
    public bool Resolved =>
        Status == StructuredResolutionStatus.Resolved &&
        Node is not null;
}

public interface IStructuredDesktopResolver
{
    StructuredResolutionResult ResolveInteractiveTarget(
        UnifiedStructuredSceneGraph graph,
        string requestedTarget,
        IReadOnlySet<string>? preferredCapabilities = null);
}

public sealed class StructuredDesktopResolver
    : IStructuredDesktopResolver
{
    public StructuredResolutionResult ResolveInteractiveTarget(
        UnifiedStructuredSceneGraph graph,
        string requestedTarget,
        IReadOnlySet<string>? preferredCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var target = Normalize(requestedTarget);
        if (target.Length == 0)
        {
            return new(
                StructuredResolutionStatus.NotFound,
                null,
                0,
                Array.Empty<StructuredResolutionCandidate>(),
                "Structured resolver không có target hợp lệ.");
        }

        var candidates =
            graph.Nodes
                .Where(node =>
                    node.Interactive &&
                    (preferredCapabilities is null ||
                     preferredCapabilities.Count == 0 ||
                     node.Capabilities.Any(preferredCapabilities.Contains)))
                .Select(node =>
                    new StructuredResolutionCandidate(
                        node,
                        Score(node, target)))
                .Where(candidate =>
                    candidate.Score >= 80)
                .OrderByDescending(candidate =>
                    candidate.Score)
                .ThenBy(candidate =>
                    candidate.Node.Depth)
                .ThenBy(candidate =>
                    candidate.Node.Id,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (candidates.Length == 0)
        {
            return new(
                StructuredResolutionStatus.NotFound,
                null,
                0,
                candidates,
                $"Không tìm thấy node structured đủ chắc chắn cho target '{requestedTarget}'.");
        }

        var best = candidates[0];

        if (candidates.Length > 1 &&
            candidates[1].Score == best.Score)
        {
            return new(
                StructuredResolutionStatus.Ambiguous,
                null,
                best.Score,
                candidates.Take(6).ToArray(),
                $"Target '{requestedTarget}' khớp nhiều node structured cùng mức tin cậy; từ chối tự đoán.");
        }

        return new(
            StructuredResolutionStatus.Resolved,
            best.Node,
            best.Score,
            candidates.Take(6).ToArray(),
            $"Đã resolve target '{requestedTarget}' thành node '{Display(best.Node)}' với score={best.Score}.");
    }

    private static int Score(
        UnifiedStructuredSceneNode node,
        string normalizedTarget)
    {
        var name = Normalize(node.Name);
        var automationId = Normalize(node.AutomationId);
        var role = Normalize(node.Role);

        if (name.Equals(
                normalizedTarget,
                StringComparison.OrdinalIgnoreCase))
            return 110;

        if (automationId.Equals(
                normalizedTarget,
                StringComparison.OrdinalIgnoreCase))
            return 105;

        if (name.Length > 0 &&
            name.Contains(
                normalizedTarget,
                StringComparison.OrdinalIgnoreCase))
            return 95;

        if (automationId.Length > 0 &&
            automationId.Contains(
                normalizedTarget.Replace(" ", string.Empty),
                StringComparison.OrdinalIgnoreCase))
            return 90;

        if (normalizedTarget.Length >= 4 &&
            name.Length >= 3 &&
            normalizedTarget.Contains(
                name,
                StringComparison.OrdinalIgnoreCase))
            return 85;

        if (role.Length > 0 &&
            $"{role} {name}".Contains(
                normalizedTarget,
                StringComparison.OrdinalIgnoreCase))
            return 80;

        return 0;
    }

    private static string Normalize(
        string value) =>
        string.Concat(
                (value ?? string.Empty)
                    .Where(character =>
                        char.IsLetterOrDigit(character) ||
                        char.IsWhiteSpace(character)))
            .Trim()
            .ToLowerInvariant();

    private static string Display(
        UnifiedStructuredSceneNode node) =>
        !string.IsNullOrWhiteSpace(node.Name)
            ? node.Name.Trim()
            : !string.IsNullOrWhiteSpace(node.AutomationId)
                ? node.AutomationId.Trim()
                : node.Role;
}
