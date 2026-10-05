namespace PersonalAI.Web.Services;

public enum StructuredVerificationStatus
{
    Verified,
    Failed,
    Inconclusive
}

public sealed record StructuredVerificationResult(
    StructuredVerificationStatus Status,
    double Confidence,
    string Reason)
{
    public bool Verified =>
        Status == StructuredVerificationStatus.Verified;
}

public interface IStructuredDesktopVerificationService
{
    StructuredVerificationResult Verify(
        DesktopOperatorDecision decision,
        UnifiedStructuredSceneGraph? graph);
}

public sealed class StructuredDesktopVerificationService
    : IStructuredDesktopVerificationService
{
    public StructuredVerificationResult Verify(
        DesktopOperatorDecision decision,
        UnifiedStructuredSceneGraph? graph)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var action = (decision.Action ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (!action.StartsWith(
                "structured-",
                StringComparison.Ordinal))
        {
            return Inconclusive(
                "Action không thuộc structured execution path.");
        }

        if (graph is null)
        {
            return Inconclusive(
                "Không có structured scene graph hậu hành động.");
        }

        if (string.IsNullOrWhiteSpace(
                decision.TargetElementId))
        {
            return Inconclusive(
                "Structured action không có TargetElementId để xác minh.");
        }

        var node = graph.Find(
            decision.TargetElementId);

        if (action is
            "structured-invoke" or
            "structured-legacy-default")
        {
            return Inconclusive(
                node is null
                    ? "Target không còn trong structured scene sau invoke; đây có thể là transition hợp lệ nhưng chưa đủ để kết luận."
                    : "Invoke/default action không có state hậu hành động chuẩn duy nhất; tiếp tục event/frame/semantic verification.");
        }

        if (node is null)
        {
            return new(
                StructuredVerificationStatus.Failed,
                0.96,
                "Target structured biến mất trong khi action yêu cầu trạng thái control cụ thể.");
        }

        if (action == "structured-select")
        {
            if (!node.IsSelected.HasValue)
                return Inconclusive(
                    "SelectionItem không expose IsSelected sau action.");

            return node.IsSelected.Value
                ? new(
                    StructuredVerificationStatus.Verified,
                    0.99,
                    "UIA SelectionItem xác nhận target đã được chọn.")
                : new(
                    StructuredVerificationStatus.Failed,
                    0.98,
                    "UIA SelectionItem xác nhận target vẫn chưa được chọn.");
        }

        if (action is
            "structured-expand" or
            "structured-collapse")
        {
            var expected =
                action == "structured-expand"
                    ? "Expanded"
                    : "Collapsed";

            if (string.IsNullOrWhiteSpace(
                    node.ExpandCollapseState))
            {
                return Inconclusive(
                    "ExpandCollapsePattern không trả state hậu hành động.");
            }

            return node.ExpandCollapseState.Equals(
                    expected,
                    StringComparison.OrdinalIgnoreCase)
                ? new(
                    StructuredVerificationStatus.Verified,
                    0.99,
                    $"UIA ExpandCollapse xác nhận state={expected}.")
                : new(
                    StructuredVerificationStatus.Failed,
                    0.98,
                    $"UIA ExpandCollapse trả state={node.ExpandCollapseState}, khác {expected}.");
        }

        if (action == "structured-toggle")
        {
            var expected =
                ReadExpectedMarker(
                    decision.ExpectedEffect,
                    "structured-state:toggle=");

            if (string.IsNullOrWhiteSpace(expected) ||
                string.IsNullOrWhiteSpace(node.ToggleState))
            {
                return Inconclusive(
                    "Structured toggle thiếu expected/current state đủ rõ để xác minh deterministic.");
            }

            return node.ToggleState.Equals(
                    expected,
                    StringComparison.OrdinalIgnoreCase)
                ? new(
                    StructuredVerificationStatus.Verified,
                    0.99,
                    $"UIA Toggle xác nhận state={expected}.")
                : new(
                    StructuredVerificationStatus.Failed,
                    0.98,
                    $"UIA Toggle trả state={node.ToggleState}, khác {expected}.");
        }

        if (action == "structured-set-value")
        {
            return Inconclusive(
                "ValuePattern write đã có sidecar readback khi execute; structured snapshot hiện chưa mang value để verify lần hai.");
        }

        if (action == "structured-focus")
        {
            return node.IsFocused
                ? new(
                    StructuredVerificationStatus.Verified,
                    0.99,
                    "UIA xác nhận target đã focus.")
                : new(
                    StructuredVerificationStatus.Failed,
                    0.97,
                    "UIA xác nhận target chưa focus sau structured-focus.");
        }

        return Inconclusive(
            $"Chưa có structured verifier deterministic cho action {action}.");
    }

    private static string ReadExpectedMarker(
        string expectedEffect,
        string marker)
    {
        var text = expectedEffect ?? string.Empty;
        var index = text.IndexOf(
            marker,
            StringComparison.OrdinalIgnoreCase);

        if (index < 0)
            return string.Empty;

        var start = index + marker.Length;
        var end = text.IndexOf(
            ';',
            start);

        if (end < 0)
            end = text.Length;

        return text[start..end].Trim();
    }

    private static StructuredVerificationResult Inconclusive(
        string reason) =>
        new(
            StructuredVerificationStatus.Inconclusive,
            0.0,
            reason);
}
