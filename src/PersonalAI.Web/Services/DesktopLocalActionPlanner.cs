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
    private static readonly IStructuredDesktopResolver StructuredResolver =
        new StructuredDesktopResolver();

    private static readonly IReadOnlySet<string> ClickCapabilities =
        new HashSet<string>(
            ["Invoke", "SelectionItem", "Toggle", "ExpandCollapse"],
            StringComparer.OrdinalIgnoreCase);

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
        ". ",
        ".\r",
        ".\n",
        "\r\n",
        "\n",
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

        if (TryPlanStructuredValueInteraction(
                goal,
                state,
                out decision))
        {
            return true;
        }

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

        if (ShouldYieldOpenApplicationStrategy(
                taskHistory))
        {
            // Loop guard đã loại chiến lược mở/focus ứng dụng trên scene hiện tại.
            // Trả quyền cho OCR/Gemini thay vì tái tạo cùng Win+S/focus action.
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

    private static bool TryPlanStructuredValueInteraction(
        string goal,
        ComputerOperatorDesktopState state,
        out DesktopOperatorDecision decision)
    {
        decision = Empty();

        if (state.ForegroundWindow is null ||
            !TryExtractStructuredValueIntent(
                goal,
                out var target,
                out var text))
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

        var valueCapabilities =
            new HashSet<string>(
                ["Value"],
                StringComparer.OrdinalIgnoreCase);

        var resolution =
            StructuredResolver.ResolveInteractiveTarget(
                graph,
                target,
                valueCapabilities);

        if (!resolution.Resolved ||
            resolution.Node is null)
        {
            return false;
        }

        var node = resolution.Node;

        if (node.IsSensitive)
            return false;

        if (node.Value is not null &&
            node.Value.Equals(
                text,
                StringComparison.Ordinal))
        {
            if (HasAdditionalGoalSteps(goal))
                return false;

            decision = Build(
                action: "complete",
                currentSubgoal:
                    $"Giá trị của '{DisplayNode(node)}' đã đúng yêu cầu.",
                expectedEffect: string.Empty,
                reason:
                    $"Structured ValuePattern readback xác nhận '{DisplayNode(node)}' đã có đúng giá trị yêu cầu; không thực hiện write dư thừa.",
                plan:
                    "Không gửi input vì structured ValuePattern đã ở trạng thái mong muốn.",
                targetLabel: DisplayNode(node),
                targetElementId: node.Id,
                coordinateWindowId: graph.WindowId,
                confidence: 0.99);

            return true;
        }

        decision = Build(
            action: "structured-set-value",
            currentSubgoal:
                $"Nhập giá trị vào '{DisplayNode(node)}' bằng UIA ValuePattern.",
            expectedEffect:
                $"Giá trị của '{DisplayNode(node)}' trở thành nội dung yêu cầu.",
            reason:
                $"Structured resolver xác định field '{target}' là node '{node.Id}' có ValuePattern với score={resolution.Score}; ưu tiên direct structured write thay vì click + keyboard.",
            plan:
                $"Set ValuePattern trên đúng target token '{node.Id}', sau đó quan sát và xác minh lại.",
            text: text,
            targetLabel: DisplayNode(node),
            targetElementId: node.Id,
            coordinateWindowId: graph.WindowId,
            imageX: node.FrameLeft + node.Width / 2,
            imageY: node.FrameTop + node.Height / 2,
            boxLeft: node.FrameLeft,
            boxTop: node.FrameTop,
            boxWidth: node.Width,
            boxHeight: node.Height,
            confidence: resolution.Score >= 100 ? 0.99 : 0.94);

        return true;
    }

    private static bool TryExtractStructuredValueIntent(
        string goal,
        out string target,
        out string text)
    {
        var value = (goal ?? string.Empty).Trim();
        var prefixes = new[]
        {
            "nhập ",
            "nhap ",
            "gõ ",
            "go ",
            "type "
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
            var separators = new[]
            {
                " vào ",
                " vao ",
                " into "
            };

            foreach (var separator in separators)
            {
                var index = remainder.IndexOf(
                    separator,
                    StringComparison.OrdinalIgnoreCase);

                if (index <= 0)
                    continue;

                text = remainder[..index]
                    .Trim()
                    .Trim('"', '\'', '“', '”');

                target = remainder[(index + separator.Length)..]
                    .Trim()
                    .Trim('"', '\'', '“', '”', '.', ':');

                target = StripStructuredTargetPrefix(target);

                return text.Length > 0 &&
                       text.Length <= 1000 &&
                       target.Length is >= 1 and <= 120;
            }
        }

        target = string.Empty;
        text = string.Empty;
        return false;
    }

    private static bool TryPlanStructuredInteraction(
        string goal,
        ComputerOperatorDesktopState state,
        out DesktopOperatorDecision decision)
    {
        decision = Empty();

        if (state.ForegroundWindow is null ||
            !TryExtractStructuredActionIntent(
                goal,
                out var requestedTarget,
                out var requestedCapability,
                out var requestedAction,
                out var desiredState))
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

        var preferredCapabilities =
            new HashSet<string>(
                [requestedCapability],
                StringComparer.OrdinalIgnoreCase);

        var resolution =
            StructuredResolver.ResolveInteractiveTarget(
                graph,
                requestedTarget,
                preferredCapabilities);

        if ((!resolution.Resolved ||
             resolution.Node is null) &&
            requestedCapability.Equals(
                "Invoke",
                StringComparison.OrdinalIgnoreCase))
        {
            resolution =
                StructuredResolver.ResolveInteractiveTarget(
                    graph,
                    requestedTarget,
                    new HashSet<string>(
                        ["LegacyIAccessible"],
                        StringComparer.OrdinalIgnoreCase));

            if (resolution.Resolved &&
                resolution.Node is not null)
            {
                requestedCapability = "LegacyIAccessible";
                requestedAction = "structured-legacy-default";
            }
        }

        if (!resolution.Resolved ||
            resolution.Node is null)
        {
            return false;
        }

        var node = resolution.Node;

        if (node.FrameLeft < 0 ||
            node.FrameTop < 0 ||
            node.FrameLeft + node.Width > state.FrameWidth ||
            node.FrameTop + node.Height > state.FrameHeight)
        {
            return false;
        }

        var actionCapability = requestedCapability;
        var structuredAction = requestedAction;

        decision = Build(
            action: structuredAction,
            currentSubgoal:
                $"Tương tác với phần tử '{DisplayNode(node)}' bằng Unified Structured Scene Graph.",
            expectedEffect:
                BuildStructuredExpectedEffect(
                    node,
                    actionCapability,
                    desiredState),
            reason:
                $"Structured resolver đã ánh xạ mục tiêu '{requestedTarget}' thành node '{node.Id}' (score={resolution.Score}, capability={actionCapability}); không cần gửi toàn màn hình cho Vision/Gemini.",
            plan:
                structuredAction.StartsWith("structured-", StringComparison.Ordinal)
                    ? $"Thực thi {structuredAction} trực tiếp bằng UIA target token '{node.Id}', sau đó quan sát lại trước hành động tiếp theo."
                    : $"Dùng bounding box đã chuẩn hóa trong scene graph của '{DisplayNode(node)}' để click an toàn rồi quan sát lại.",
            targetLabel: DisplayNode(node),
            targetElementId: node.Id,
            coordinateWindowId: graph.WindowId,
            imageX: node.FrameLeft + node.Width / 2,
            imageY: node.FrameTop + node.Height / 2,
            boxLeft: node.FrameLeft,
            boxTop: node.FrameTop,
            boxWidth: node.Width,
            boxHeight: node.Height,
            confidence: resolution.Score >= 100 ? 0.99 : 0.94);

        return true;
    }

    private static bool TryExtractStructuredActionIntent(
        string goal,
        out string target,
        out string capability,
        out string action,
        out string desiredState)
    {
        var value = (goal ?? string.Empty).Trim();

        var intents = new[]
        {
            new { Prefix = "mở rộng ", Capability = "ExpandCollapse", Action = "structured-expand", DesiredState = "Expanded" },
            new { Prefix = "mo rong ", Capability = "ExpandCollapse", Action = "structured-expand", DesiredState = "Expanded" },
            new { Prefix = "expand ", Capability = "ExpandCollapse", Action = "structured-expand", DesiredState = "Expanded" },
            new { Prefix = "thu gọn ", Capability = "ExpandCollapse", Action = "structured-collapse", DesiredState = "Collapsed" },
            new { Prefix = "thu gon ", Capability = "ExpandCollapse", Action = "structured-collapse", DesiredState = "Collapsed" },
            new { Prefix = "collapse ", Capability = "ExpandCollapse", Action = "structured-collapse", DesiredState = "Collapsed" },
            new { Prefix = "bật ", Capability = "Toggle", Action = "structured-toggle", DesiredState = "On" },
            new { Prefix = "bat ", Capability = "Toggle", Action = "structured-toggle", DesiredState = "On" },
            new { Prefix = "tắt ", Capability = "Toggle", Action = "structured-toggle", DesiredState = "Off" },
            new { Prefix = "tat ", Capability = "Toggle", Action = "structured-toggle", DesiredState = "Off" },
            new { Prefix = "tick ", Capability = "Toggle", Action = "structured-toggle", DesiredState = "On" },
            new { Prefix = "toggle ", Capability = "Toggle", Action = "structured-toggle", DesiredState = "" },
            new { Prefix = "chọn ", Capability = "SelectionItem", Action = "structured-select", DesiredState = "Selected" },
            new { Prefix = "chon ", Capability = "SelectionItem", Action = "structured-select", DesiredState = "Selected" },
            new { Prefix = "select ", Capability = "SelectionItem", Action = "structured-select", DesiredState = "Selected" },
            new { Prefix = "bấm ", Capability = "Invoke", Action = "structured-invoke", DesiredState = "" },
            new { Prefix = "bam ", Capability = "Invoke", Action = "structured-invoke", DesiredState = "" },
            new { Prefix = "nhấn ", Capability = "Invoke", Action = "structured-invoke", DesiredState = "" },
            new { Prefix = "nhan ", Capability = "Invoke", Action = "structured-invoke", DesiredState = "" },
            new { Prefix = "click ", Capability = "Invoke", Action = "structured-invoke", DesiredState = "" },
            new { Prefix = "press ", Capability = "Invoke", Action = "structured-invoke", DesiredState = "" }
        };

        foreach (var intent in intents)
        {
            if (!value.StartsWith(
                    intent.Prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = value[intent.Prefix.Length..].Trim();
            remainder = StripStructuredTargetPrefix(remainder);

            var cut = remainder.Length;
            foreach (var separator in NextStepSeparators)
            {
                var index = remainder.IndexOf(
                    separator,
                    StringComparison.OrdinalIgnoreCase);

                if (index >= 0 &&
                    index < cut)
                {
                    cut = index;
                }
            }

            target = remainder[..cut]
                .Trim()
                .Trim('"', '\'', '“', '”', '.', ':');

            capability = intent.Capability;
            action = intent.Action;
            desiredState = intent.DesiredState;

            return target.Length is >= 1 and <= 120;
        }

        target = string.Empty;
        capability = string.Empty;
        action = string.Empty;
        desiredState = string.Empty;
        return false;
    }

    private static string BuildStructuredExpectedEffect(
        UnifiedStructuredSceneNode node,
        string capability,
        string desiredState)
    {
        var marker =
            capability.Equals(
                "Toggle",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(desiredState)
                ? $"structured-state:toggle={desiredState}; "
                : capability.Equals(
                    "ExpandCollapse",
                    StringComparison.OrdinalIgnoreCase) &&
                  !string.IsNullOrWhiteSpace(desiredState)
                    ? $"structured-state:expandCollapse={desiredState}; "
                    : capability.Equals(
                        "SelectionItem",
                        StringComparison.OrdinalIgnoreCase) &&
                      desiredState.Equals(
                          "Selected",
                          StringComparison.OrdinalIgnoreCase)
                        ? "structured-state:selected=true; "
                        : string.Empty;

        return
            $"{marker}Phần tử '{DisplayNode(node)}' phản hồi sau thao tác {capability}.";
    }

    private static bool CanSafelyApplyRequestedStructuredState(
        UnifiedStructuredSceneNode node,
        string capability,
        string desiredState,
        out bool alreadySatisfied)
    {
        alreadySatisfied = false;

        if (string.IsNullOrWhiteSpace(desiredState))
            return true;

        if (capability.Equals(
                "Toggle",
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(node.ToggleState) ||
                node.ToggleState.Equals(
                    "Indeterminate",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            alreadySatisfied =
                node.ToggleState.Equals(
                    desiredState,
                    StringComparison.OrdinalIgnoreCase);

            return true;
        }

        if (capability.Equals(
                "ExpandCollapse",
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(node.ExpandCollapseState) ||
                node.ExpandCollapseState.Equals(
                    "LeafNode",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            alreadySatisfied =
                node.ExpandCollapseState.Equals(
                    desiredState,
                    StringComparison.OrdinalIgnoreCase);

            return true;
        }

        if (capability.Equals(
                "SelectionItem",
                StringComparison.OrdinalIgnoreCase) &&
            desiredState.Equals(
                "Selected",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!node.IsSelected.HasValue)
                return false;

            alreadySatisfied = node.IsSelected.Value;
            return true;
        }

        return true;
    }

    private static bool ShouldYieldOpenApplicationStrategy(
        string taskHistory)
    {
        if (string.IsNullOrWhiteSpace(
                taskHistory))
        {
            return false;
        }

        return
            taskHistory.Contains(
                "CHỈ DẪN THOÁT VÒNG LẶP",
                StringComparison.OrdinalIgnoreCase) ||
            taskHistory.Contains(
                "BẮT BUỘC đổi chiến lược",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAdditionalGoalSteps(
        string goal) =>
        NextStepSeparators.Any(separator =>
            (goal ?? string.Empty).IndexOf(
                separator,
                StringComparison.OrdinalIgnoreCase) >= 0);

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
