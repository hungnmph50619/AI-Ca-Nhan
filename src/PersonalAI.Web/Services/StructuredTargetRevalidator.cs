namespace PersonalAI.Web.Services;

public enum StructuredTargetRevalidationStatus
{
    Valid,
    Remapped,
    Rejected
}

public sealed record StructuredTargetRevalidationResult(
    StructuredTargetRevalidationStatus Status,
    DesktopOperatorDecision Decision,
    double Confidence,
    string Reason)
{
    public bool SafeToExecute =>
        Status is
            StructuredTargetRevalidationStatus.Valid or
            StructuredTargetRevalidationStatus.Remapped;
}

public sealed class StructuredTargetRevalidator
{
    private static readonly IStructuredDesktopResolver Resolver =
        new StructuredDesktopResolver();

    private static readonly TimeSpan MaximumSceneAge =
        TimeSpan.FromSeconds(4);

    public StructuredTargetRevalidationResult Revalidate(
        DesktopOperatorDecision decision,
        UnifiedStructuredSceneGraph? graph,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var action =
            (decision.Action ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        if (!action.StartsWith(
                "structured-",
                StringComparison.Ordinal))
        {
            return new(
                StructuredTargetRevalidationStatus.Valid,
                decision,
                1.0,
                "Action không thuộc structured path; không cần structured target revalidation.");
        }

        if (graph is null)
        {
            return Reject(
                decision,
                "Không có structured scene graph mới ngay trước execute.");
        }

        var age =
            nowUtc - graph.CapturedAtUtc;

        if (age < TimeSpan.Zero ||
            age > MaximumSceneAge)
        {
            return Reject(
                decision,
                $"Structured scene đã stale ({Math.Max(0, age.TotalMilliseconds):0} ms); bắt buộc observe/replan.");
        }

        if (!string.IsNullOrWhiteSpace(
                decision.CoordinateWindowId) &&
            !graph.WindowId.Equals(
                decision.CoordinateWindowId,
                StringComparison.OrdinalIgnoreCase))
        {
            return Reject(
                decision,
                "Structured scene mới thuộc cửa sổ khác với cửa sổ đã plan.");
        }

        var requiredCapability =
            CapabilityForAction(action);

        if (requiredCapability.Length == 0)
        {
            return Reject(
                decision,
                $"Không xác định được capability cho structured action {action}.");
        }

        var exact =
            graph.Find(
                decision.TargetElementId ?? string.Empty);

        if (exact is not null &&
            exact.Interactive &&
            exact.Capabilities.Contains(
                requiredCapability,
                StringComparer.OrdinalIgnoreCase))
        {
            return new(
                StructuredTargetRevalidationStatus.Valid,
                decision,
                CapabilityConfidence(
                    exact,
                    requiredCapability),
                $"Target token vẫn hợp lệ trong scene mới; capability={requiredCapability}.");
        }

        var label =
            (decision.TargetLabel ?? string.Empty)
                .Trim();

        if (label.Length == 0)
        {
            return Reject(
                decision,
                "Target token đã đổi nhưng decision không có semantic label để remap an toàn.");
        }

        var resolution =
            Resolver.ResolveInteractiveTarget(
                graph,
                label,
                new HashSet<string>(
                    [requiredCapability],
                    StringComparer.OrdinalIgnoreCase));

        if (!resolution.Resolved ||
            resolution.Node is null)
        {
            return Reject(
                decision,
                $"Target token cũ không còn hợp lệ và semantic remap không duy nhất: {resolution.Reason}");
        }

        var node =
            resolution.Node;

        var remapped =
            decision with
            {
                TargetElementId = node.Id,
                CoordinateWindowId = graph.WindowId,
                ImageX = node.FrameLeft + node.Width / 2,
                ImageY = node.FrameTop + node.Height / 2,
                BoxLeft = node.FrameLeft,
                BoxTop = node.FrameTop,
                BoxWidth = node.Width,
                BoxHeight = node.Height
            };

        return new(
            StructuredTargetRevalidationStatus.Remapped,
            remapped,
            Math.Min(
                resolution.Score >= 100
                    ? 0.98
                    : 0.93,
                CapabilityConfidence(
                    node,
                    requiredCapability)),
            $"Target token đã đổi nhưng semantic resolver remap duy nhất '{label}' sang '{node.Id}' với capability={requiredCapability}.");
    }

    private static string CapabilityForAction(
        string action) =>
        action switch
        {
            "structured-focus" => "Value",
            "structured-invoke" => "Invoke",
            "structured-select" => "SelectionItem",
            "structured-toggle" => "Toggle",
            "structured-expand" or
            "structured-collapse" => "ExpandCollapse",
            "structured-set-value" => "Value",
            "structured-legacy-default" => "LegacyIAccessible",
            _ => string.Empty
        };

    private static double CapabilityConfidence(
        UnifiedStructuredSceneNode node,
        string capability) =>
        capability.Equals(
            "LegacyIAccessible",
            StringComparison.OrdinalIgnoreCase)
            ? 0.78
            : node.IsSensitive
                ? 0.90
                : 0.98;

    private static StructuredTargetRevalidationResult Reject(
        DesktopOperatorDecision decision,
        string reason) =>
        new(
            StructuredTargetRevalidationStatus.Rejected,
            decision,
            0.0,
            reason);
}
