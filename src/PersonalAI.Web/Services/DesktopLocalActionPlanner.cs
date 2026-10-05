using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDesktopLocalActionPlanner
{
    bool TryPlan(
        string goal,
        ComputerOperatorDesktopState state,
        string taskHistory,
        out DesktopOperatorDecision decision);
}

public sealed class DesktopLocalActionPlanner
    : IDesktopLocalActionPlanner
{
    private static readonly string[] OpenPrefixes =
    [
        "mở ",
        "mo ",
        "open ",
        "launch ",
        "khởi động ",
        "khoi dong "
    ];

    private static readonly string[] NextStepSeparators =
    [
        ",",
        ";",
        " sau đó ",
        " sau do ",
        " rồi ",
        " roi ",
        " then ",
        " and then "
    ];

    public bool TryPlan(
        string goal,
        ComputerOperatorDesktopState state,
        string taskHistory,
        out DesktopOperatorDecision decision)
    {
        ArgumentNullException.ThrowIfNull(state);

        decision = Empty();

        if (TryPlanStructuredInteraction(
                goal,
                state,
                out decision))
        {
            return true;
        }

        if (!TryExtractOpenApplicationTarget(
                goal,
                out var target))
        {
            return false;
        }

        var matchingWindow =
            state.Windows
                .Where(window =>
                    WindowMatchesTarget(
                        window,
                        target))
                .OrderByDescending(window =>
                    window.IsForeground)
                .FirstOrDefault();

        if (matchingWindow is not null)
        {
            if (matchingWindow.IsForeground)
            {
                // Local planner chỉ chịu trách nhiệm đưa ứng dụng lên foreground.
                // Các bước nghiệp vụ tiếp theo để planner cấp cao xử lý.
                return false;
            }

            decision = Build(
                action: "focus-window",
                query: target,
                currentSubgoal: $"Đưa {target} lên foreground.",
                expectedEffect: $"Cửa sổ {target} trở thành foreground.",
                reason: $"Local planner tìm thấy cửa sổ phù hợp với mục tiêu '{target}', nên ưu tiên focus trực tiếp thay vì gọi Vision.",
                plan: $"Focus cửa sổ {target} bằng capability Windows hiện có.");

            return true;
        }

        var shellSearchActive =
            IsWindowsSearchSurface(
                state.ForegroundWindow);

        var typedMarker =
            $"LOCAL-SHELL-TYPED:{target}";

        if (shellSearchActive &&
            taskHistory.Contains(
                typedMarker,
                StringComparison.OrdinalIgnoreCase))
        {
            decision = Build(
                action: "press-key",
                key: "ENTER",
                currentSubgoal: $"Mở {target} từ kết quả Windows Search.",
                expectedEffect: $"Windows Search khởi chạy ứng dụng phù hợp với từ khóa {target}.",
                reason: "Local planner đã có bằng chứng từ lịch sử rằng từ khóa tìm ứng dụng đã được nhập; bước generic tiếp theo là Enter.",
                plan: "Nhấn Enter một lần rồi quan sát cửa sổ foreground mới.");

            return true;
        }

        if (shellSearchActive)
        {
            decision = Build(
                action: "type-text",
                text: target,
                currentSubgoal: $"Tìm {target} bằng Windows Search.",
                expectedEffect: $"LOCAL-SHELL-TYPED:{target}",
                reason: "Windows Search đang ở foreground; local planner có thể nhập tên ứng dụng bằng Generic Text Interaction Engine mà không cần Vision.",
                plan: $"Nhập toàn bộ từ khóa {target} vào vùng tìm kiếm hiện tại.");

            return true;
        }

        decision = Build(
            action: "press-hotkey",
            keys: ["WIN", "S"],
            currentSubgoal: $"Mở Windows Search để tìm {target}.",
            expectedEffect: "Windows Search xuất hiện và nhận focus.",
            reason: $"Mục tiêu hiện tại là mở một ứng dụng chưa có cửa sổ phù hợp. Dùng capability Windows Search generic thay vì suy luận tọa độ hoặc gọi lại Vision trên cùng scene.",
            plan: "Mở Windows Search bằng WIN+S, sau đó quan sát lại trước hành động tiếp theo.");

        return true;
    }

    private static bool TryPlanStructuredInteraction(
        string goal,
        ComputerOperatorDesktopState state,
        out DesktopOperatorDecision decision)
    {
        decision = Empty();

        if (state.ForegroundWindow is null ||
            !TryExtractStructuredClickTarget(
                goal,
                out var requestedTarget))
        {
            return false;
        }

        var graph =
            state.StructuredGraph ??
            UnifiedStructuredSceneGraphBuilder.Build(
                state.StructuredScene,
                state.ForegroundWindow,
                state.FrameLeft,
                state.FrameTop,
                state.FrameWidth,
                state.FrameHeight);

        if (graph is null)
            return false;

        var candidates =
            graph.Nodes
                .Where(node =>
                    node.Interactive &&
                    node.Capabilities.Any(capability =>
                        capability.Equals("Invoke", StringComparison.OrdinalIgnoreCase) ||
                        capability.Equals("SelectionItem", StringComparison.OrdinalIgnoreCase) ||
                        capability.Equals("Toggle", StringComparison.OrdinalIgnoreCase) ||
                        capability.Equals("ExpandCollapse", StringComparison.OrdinalIgnoreCase)))
                .Select(node => new
                {
                    Node = node,
                    Score = ScoreStructuredTarget(
                        node,
                        requestedTarget)
                })
                .Where(item => item.Score >= 80)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Node.Depth)
                .ToArray();

        if (candidates.Length == 0)
            return false;

        var best = candidates[0];
        if (candidates.Length > 1 &&
            candidates[1].Score == best.Score)
        {
            return false;
        }

        var node = best.Node;

        if (node.FrameLeft < 0 ||
            node.FrameTop < 0 ||
            node.FrameLeft + node.Width > state.FrameWidth ||
            node.FrameTop + node.Height > state.FrameHeight)
        {
            return false;
        }

        var actionCapability =
            node.Capabilities.FirstOrDefault(capability =>
                capability.Equals("Invoke", StringComparison.OrdinalIgnoreCase) ||
                capability.Equals("SelectionItem", StringComparison.OrdinalIgnoreCase) ||
                capability.Equals("Toggle", StringComparison.OrdinalIgnoreCase) ||
                capability.Equals("ExpandCollapse", StringComparison.OrdinalIgnoreCase))
            ?? "UIA";

        decision = Build(
            action: "click-left",
            currentSubgoal:
                $"Tương tác với phần tử '{DisplayNode(node)}' bằng Unified Structured Scene Graph.",
            expectedEffect:
                $"Phần tử '{DisplayNode(node)}' phản hồi sau thao tác {actionCapability}.",
            reason:
                $"Structured-first planner tìm thấy duy nhất một node trong scene graph phù hợp với mục tiêu '{requestedTarget}' (score={best.Score}, capability={actionCapability}); không cần gửi toàn màn hình cho Vision/Gemini.",
            plan:
                $"Dùng bounding box đã chuẩn hóa trong scene graph của '{DisplayNode(node)}' để click an toàn rồi quan sát lại.",
            targetLabel: DisplayNode(node),
            targetElementId: node.Id,
            coordinateWindowId: graph.WindowId,
            imageX: node.FrameLeft + node.Width / 2,
            imageY: node.FrameTop + node.Height / 2,
            boxLeft: node.FrameLeft,
            boxTop: node.FrameTop,
            boxWidth: node.Width,
            boxHeight: node.Height,
            confidence: best.Score >= 100 ? 0.99 : 0.94);

        return true;
    }

    private static bool TryExtractStructuredClickTarget(
        string goal,
        out string target)
    {
        var value = (goal ?? string.Empty).Trim();
        var prefixes = new[]
        {
            "bấm ",
            "bam ",
            "nhấn ",
            "nhan ",
            "click ",
            "press ",
            "chọn ",
            "chon ",
            "select ",
            "tick "
        };

        foreach (var prefix in prefixes)
        {
            if (!value.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = value[prefix.Length..].Trim();
            remainder = StripStructuredTargetPrefix(remainder);

            foreach (var separator in NextStepSeparators)
            {
                var index = remainder.IndexOf(
                    separator,
                    StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                    remainder = remainder[..index];
            }

            target = remainder
                .Trim()
                .Trim('"', '\'', '“', '”', '.', ':');

            return target.Length is >= 1 and <= 120;
        }

        target = string.Empty;
        return false;
    }

    private static string StripStructuredTargetPrefix(
        string value)
    {
        var prefixes = new[]
        {
            "nút ",
            "nut ",
            "button ",
            "mục ",
            "muc ",
            "option ",
            "checkbox "
        };

        foreach (var prefix in prefixes)
        {
            if (value.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return value[prefix.Length..].Trim();
            }
        }

        return value;
    }

    private static int ScoreStructuredTarget(
        UnifiedStructuredSceneNode node,
        string requestedTarget)
    {
        var target = Normalize(requestedTarget);
        if (target.Length == 0)
            return 0;

        var name = Normalize(node.Name);
        var automationId = Normalize(node.AutomationId);
        var role = Normalize(node.Role);

        if (name.Equals(target, StringComparison.OrdinalIgnoreCase))
            return 110;

        if (automationId.Equals(target, StringComparison.OrdinalIgnoreCase))
            return 105;

        if (name.Length > 0 &&
            name.Contains(target, StringComparison.OrdinalIgnoreCase))
            return 95;

        if (automationId.Length > 0 &&
            automationId.Contains(
                target.Replace(" ", string.Empty),
                StringComparison.OrdinalIgnoreCase))
            return 90;

        if (target.Length >= 4 &&
            target.Contains(name, StringComparison.OrdinalIgnoreCase) &&
            name.Length >= 3)
            return 85;

        if (role.Length > 0 &&
            $"{role} {name}".Contains(
                target,
                StringComparison.OrdinalIgnoreCase))
            return 80;

        return 0;
    }

    private static string DisplayNode(
        UnifiedStructuredSceneNode node) =>
        !string.IsNullOrWhiteSpace(node.Name)
            ? node.Name.Trim()
            : !string.IsNullOrWhiteSpace(node.AutomationId)
                ? node.AutomationId.Trim()
                : node.Role;

    private static bool TryExtractOpenApplicationTarget(
        string goal,
        out string target)
    {
        var value =
            (goal ?? string.Empty)
                .Trim();

        foreach (var prefix in OpenPrefixes)
        {
            if (!value.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder =
                value[prefix.Length..]
                    .Trim();

            var cut =
                remainder.Length;

            foreach (var separator in NextStepSeparators)
            {
                var index =
                    remainder.IndexOf(
                        separator,
                        StringComparison.OrdinalIgnoreCase);

                if (index >= 0 &&
                    index < cut)
                {
                    cut = index;
                }
            }

            target =
                remainder[..cut]
                    .Trim()
                    .Trim('"', '\'', '“', '”', '.', ':');

            if (target.Length is >= 2 and <= 96)
                return true;

            break;
        }

        target = string.Empty;
        return false;
    }

    private static bool WindowMatchesTarget(
        ComputerWindowInfo window,
        string target)
    {
        var normalizedTarget =
            Normalize(target);

        if (normalizedTarget.Length < 2)
            return false;

        var title =
            Normalize(
                window.Title);

        var process =
            Normalize(
                window.ProcessName ?? string.Empty)
                .Replace(
                    "exe",
                    string.Empty,
                    StringComparison.Ordinal);

        return title.Contains(
                   normalizedTarget,
                   StringComparison.OrdinalIgnoreCase) ||
               process.Contains(
                   normalizedTarget.Replace(" ", string.Empty),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowsSearchSurface(
        ComputerWindowInfo? window)
    {
        if (window is null)
            return false;

        var process =
            (window.ProcessName ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        var title =
            (window.Title ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        return process.Contains("searchhost") ||
               process.Contains("searchapp") ||
               process.Contains("startmenuexperiencehost") ||
               title.Equals("search", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("windows search", StringComparison.OrdinalIgnoreCase);
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

    private static DesktopOperatorDecision Build(
        string action,
        string currentSubgoal,
        string expectedEffect,
        string reason,
        string plan,
        string query = "",
        string text = "",
        string key = "",
        IReadOnlyList<string>? keys = null,
        string targetLabel = "",
        string targetElementId = "",
        string coordinateWindowId = "",
        int imageX = 0,
        int imageY = 0,
        int boxLeft = 0,
        int boxTop = 0,
        int boxWidth = 0,
        int boxHeight = 0,
        double confidence = 0.97) =>
        new(
            State: "Local planner đang dùng capability Windows có bằng chứng trực tiếp.",
            Plan: plan,
            CurrentSubgoal: currentSubgoal,
            GoalProgress: 0,
            VerifiedMilestones: Array.Empty<string>(),
            Action: action,
            Query: query,
            Text: text,
            Key: key,
            Keys: keys ?? Array.Empty<string>(),
            Url: string.Empty,
            TargetLabel: targetLabel,
            CoordinateSpace: ComputerCoordinateSpaces.ImagePixel,
            CoordinateWindowId: coordinateWindowId,
            ImageX: imageX,
            ImageY: imageY,
            EndImageX: 0,
            EndImageY: 0,
            NormalizedX: 0,
            NormalizedY: 0,
            EndNormalizedX: 0,
            EndNormalizedY: 0,
            BoxLeft: boxLeft,
            BoxTop: boxTop,
            BoxWidth: boxWidth,
            BoxHeight: boxHeight,
            BoxNormalizedLeft: 0,
            BoxNormalizedTop: 0,
            BoxNormalizedWidth: 0,
            BoxNormalizedHeight: 0,
            ScrollDelta: 0,
            ExpectedEffect: expectedEffect,
            Confidence: confidence,
            Reason: reason,
            SceneElements: Array.Empty<DesktopSceneElement>(),
            TargetElementId: targetElementId);

    private static DesktopOperatorDecision Empty() =>
        Build(
            action: "wait",
            currentSubgoal: string.Empty,
            expectedEffect: string.Empty,
            reason: string.Empty,
            plan: string.Empty);
}
