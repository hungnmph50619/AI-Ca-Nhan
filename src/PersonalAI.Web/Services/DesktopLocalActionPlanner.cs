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
        IReadOnlyList<string>? keys = null) =>
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
            TargetLabel: string.Empty,
            CoordinateSpace: ComputerCoordinateSpaces.ImagePixel,
            CoordinateWindowId: string.Empty,
            ImageX: 0,
            ImageY: 0,
            EndImageX: 0,
            EndImageY: 0,
            NormalizedX: 0,
            NormalizedY: 0,
            EndNormalizedX: 0,
            EndNormalizedY: 0,
            BoxLeft: 0,
            BoxTop: 0,
            BoxWidth: 0,
            BoxHeight: 0,
            BoxNormalizedLeft: 0,
            BoxNormalizedTop: 0,
            BoxNormalizedWidth: 0,
            BoxNormalizedHeight: 0,
            ScrollDelta: 0,
            ExpectedEffect: expectedEffect,
            Confidence: 0.97,
            Reason: reason,
            SceneElements: Array.Empty<DesktopSceneElement>(),
            TargetElementId: string.Empty);

    private static DesktopOperatorDecision Empty() =>
        Build(
            action: "wait",
            currentSubgoal: string.Empty,
            expectedEffect: string.Empty,
            reason: string.Empty,
            plan: string.Empty);
}
