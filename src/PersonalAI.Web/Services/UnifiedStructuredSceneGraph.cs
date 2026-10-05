using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record UnifiedStructuredSceneNode(
    string Id,
    string ParentId,
    int Depth,
    string Role,
    string Name,
    string AutomationId,
    string ClassName,
    bool IsEnabled,
    bool IsFocused,
    bool IsVisible,
    int DesktopLeft,
    int DesktopTop,
    int Width,
    int Height,
    int FrameLeft,
    int FrameTop,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Children)
{
    public bool Interactive =>
        IsVisible &&
        IsEnabled &&
        Width > 3 &&
        Height > 3 &&
        Capabilities.Count > 0;
}

public sealed record UnifiedStructuredSceneGraph(
    string WindowId,
    string WindowTitle,
    string ProcessName,
    string RootId,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<UnifiedStructuredSceneNode> Nodes,
    string Source,
    string Detail)
{
    public int NodeCount => Nodes.Count;

    public int InteractiveNodeCount =>
        Nodes.Count(node => node.Interactive);

    public UnifiedStructuredSceneNode? Find(
        string id) =>
        Nodes.FirstOrDefault(node =>
            node.Id.Equals(
                id,
                StringComparison.OrdinalIgnoreCase));
}

public static class UnifiedStructuredSceneGraphBuilder
{
    private static readonly IReadOnlySet<string> SupportedCapabilities =
        new HashSet<string>(
            [
                "Value",
                "Invoke",
                "SelectionItem",
                "Toggle",
                "ExpandCollapse"
            ],
            StringComparer.OrdinalIgnoreCase);

    public static UnifiedStructuredSceneGraph? Build(
        StructuredDesktopSnapshot? snapshot,
        ComputerWindowInfo? foregroundWindow,
        int frameLeft,
        int frameTop,
        int frameWidth,
        int frameHeight)
    {
        if (snapshot is null ||
            foregroundWindow is null ||
            snapshot.Nodes.Count == 0)
        {
            return null;
        }

        var childrenByParent =
            snapshot.Nodes
                .Where(node =>
                    !string.IsNullOrWhiteSpace(node.ParentToken))
                .GroupBy(
                    node => node.ParentToken,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group
                        .Select(node => node.Token)
                        .Where(token => !string.IsNullOrWhiteSpace(token))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);

        var nodes =
            snapshot.Nodes
                .Where(node =>
                    !string.IsNullOrWhiteSpace(node.Token))
                .Select(node =>
                {
                    var relativeLeft =
                        node.Left - frameLeft;
                    var relativeTop =
                        node.Top - frameTop;

                    var visible =
                        !node.IsOffscreen &&
                        node.Width > 0 &&
                        node.Height > 0 &&
                        relativeLeft + node.Width > 0 &&
                        relativeTop + node.Height > 0 &&
                        relativeLeft < frameWidth &&
                        relativeTop < frameHeight;

                    var capabilities =
                        node.Patterns
                            .Where(pattern =>
                                SupportedCapabilities.Contains(pattern))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(pattern => pattern)
                            .ToArray();

                    return new UnifiedStructuredSceneNode(
                        Id: node.Token,
                        ParentId: node.ParentToken,
                        Depth: node.Depth,
                        Role: node.Role,
                        Name: node.Name,
                        AutomationId: node.AutomationId,
                        ClassName: node.ClassName,
                        IsEnabled: node.IsEnabled,
                        IsFocused: node.IsFocused,
                        IsVisible: visible,
                        DesktopLeft: node.Left,
                        DesktopTop: node.Top,
                        Width: node.Width,
                        Height: node.Height,
                        FrameLeft: relativeLeft,
                        FrameTop: relativeTop,
                        Capabilities: capabilities,
                        Children: childrenByParent.TryGetValue(
                            node.Token,
                            out var children)
                                ? children
                                : Array.Empty<string>());
                })
                .ToArray();

        return new UnifiedStructuredSceneGraph(
            WindowId: snapshot.WindowId,
            WindowTitle: foregroundWindow.Title,
            ProcessName: foregroundWindow.ProcessName ?? string.Empty,
            RootId: snapshot.RootToken,
            CapturedAtUtc: snapshot.CapturedAtUtc,
            Nodes: nodes,
            Source: snapshot.Source,
            Detail: snapshot.Detail);
    }
}
