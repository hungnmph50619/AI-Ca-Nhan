using System.Text.Json;

namespace PersonalAI.Web.Services;

public sealed record StructuredDesktopNode(
    string Token,
    string ParentToken,
    int Depth,
    string Role,
    string Name,
    string AutomationId,
    string ClassName,
    bool IsEnabled,
    bool IsFocused,
    bool IsOffscreen,
    int Left,
    int Top,
    int Width,
    int Height,
    IReadOnlyList<string> Patterns,
    string ToggleState = "",
    string ExpandCollapseState = "",
    bool? IsSelected = null,
    string? Value = null,
    bool IsSensitive = false);

public sealed record StructuredDesktopSnapshot(
    string WindowId,
    string RootToken,
    DateTimeOffset CapturedAtUtc,
    int NodeCount,
    int MaximumNodes,
    int MaximumDepth,
    IReadOnlyList<StructuredDesktopNode> Nodes,
    string Source,
    string Detail);

public interface IStructuredDesktopSnapshotService
{
    StructuredDesktopSnapshot? CaptureWindow(
        string windowId,
        int maximumNodes = 200,
        int maximumDepth = 6);
}

public sealed class StructuredDesktopSnapshotService(
    IFlaUiAutomationClient flaUi)
    : IStructuredDesktopSnapshotService
{
    public StructuredDesktopSnapshot? CaptureWindow(
        string windowId,
        int maximumNodes = 200,
        int maximumDepth = 6)
    {
        if (string.IsNullOrWhiteSpace(windowId) ||
            flaUi.Available != true)
        {
            return null;
        }

        var response =
            flaUi.Invoke(
                new FlaUiAutomationRequest(
                    Operation: "read-uia-tree",
                    WindowId: windowId,
                    MaxNodes: Math.Clamp(
                        maximumNodes,
                        20,
                        1000),
                    MaxDepth: Math.Clamp(
                        maximumDepth,
                        1,
                        12)));

        if (!response.Success ||
            string.IsNullOrWhiteSpace(
                response.StructuredJson))
        {
            return null;
        }

        SidecarStructuredSnapshot? snapshot;
        try
        {
            snapshot =
                JsonSerializer.Deserialize<SidecarStructuredSnapshot>(
                    response.StructuredJson,
                    JsonOptions());
        }
        catch (JsonException)
        {
            return null;
        }

        if (snapshot is null ||
            snapshot.Nodes is null)
        {
            return null;
        }

        var nodes =
            snapshot.Nodes
                .Select(node =>
                    new StructuredDesktopNode(
                        node.Token ?? string.Empty,
                        node.ParentToken ?? string.Empty,
                        node.Depth,
                        node.Role ?? "Unknown",
                        node.Name ?? string.Empty,
                        node.AutomationId ?? string.Empty,
                        node.ClassName ?? string.Empty,
                        node.IsEnabled,
                        node.IsFocused,
                        node.IsOffscreen,
                        node.Left,
                        node.Top,
                        node.Width,
                        node.Height,
                        node.Patterns ?? Array.Empty<string>(),
                        node.ToggleState ?? string.Empty,
                        node.ExpandCollapseState ?? string.Empty,
                        node.IsSelected,
                        node.Value,
                        node.IsSensitive))
                .ToArray();

        return new(
            snapshot.WindowId ?? windowId,
            snapshot.RootToken ?? string.Empty,
            snapshot.CapturedAtUtc,
            nodes.Length,
            snapshot.MaximumNodes,
            snapshot.MaximumDepth,
            nodes,
            "flaui-uia3",
            response.Detail);
    }

    private static JsonSerializerOptions JsonOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private sealed record SidecarStructuredSnapshot(
        string? WindowId,
        string? RootToken,
        DateTimeOffset CapturedAtUtc,
        int NodeCount,
        int MaximumNodes,
        int MaximumDepth,
        SidecarStructuredNode[]? Nodes);

    private sealed record SidecarStructuredNode(
        string? Token,
        string? ParentToken,
        int Depth,
        string? Role,
        string? Name,
        string? AutomationId,
        string? ClassName,
        bool IsEnabled,
        bool IsFocused,
        bool IsOffscreen,
        int Left,
        int Top,
        int Width,
        int Height,
        string[]? Patterns,
        string? ToggleState = null,
        string? ExpandCollapseState = null,
        bool? IsSelected = null,
        string? Value = null,
        bool IsSensitive = false);
}
