using System.Net;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerOperatorProviderResiliencePolicy
{
    bool IsVisionProviderExhausted(Exception exception);

    bool IsTransientVisionFailure(HttpRequestException exception);

    int CalculateCooldownDelayMilliseconds(
        DateTimeOffset backoffUntil,
        DateTimeOffset now);

    bool IsDegradedPlannerDecision(
        DesktopOperatorDecision decision);

    DesktopOperatorDecision CreateProviderUnavailableBlockedDecision();

    DesktopOperatorDecision CreatePlannerCooldownWaitDecision(
        DateTimeOffset backoffUntil);
}

/// <summary>
/// Chính sách resilience cho semantic/Vision provider.
/// Chỉ phân loại lỗi/cooldown và tạo decision chờ hoặc blocked an toàn.
/// Không gọi provider, không execute action và không thay thế Decision Authority.
/// </summary>
public sealed class ComputerOperatorProviderResiliencePolicy
    : IComputerOperatorProviderResiliencePolicy
{
    public bool IsVisionProviderExhausted(
        Exception exception) =>
        exception is InvalidOperationException &&
        exception.Message.Contains(
            "Computer Operator vision provider",
            StringComparison.OrdinalIgnoreCase) &&
        (exception.Message.Contains(
             "đều thất bại",
             StringComparison.OrdinalIgnoreCase) ||
         exception.Message.Contains(
             "nào được cấu hình",
             StringComparison.OrdinalIgnoreCase));

    public bool IsTransientVisionFailure(
        HttpRequestException exception)
    {
        if (exception.StatusCode is null)
            return true;

        var code =
            (int)exception.StatusCode.Value;

        return code is
            408 or
            429 or
            500 or
            502 or
            503 or
            504;
    }

    public int CalculateCooldownDelayMilliseconds(
        DateTimeOffset backoffUntil,
        DateTimeOffset now) =>
        Math.Clamp(
            (int)Math.Ceiling(
                (backoffUntil - now)
                .TotalMilliseconds),
            900,
            5000);

    public bool IsDegradedPlannerDecision(
        DesktopOperatorDecision decision)
    {
        if (!decision.Action.Equals(
                "wait",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var reason =
            decision.Reason ?? string.Empty;

        return reason.Contains(
                   "Gemini planning tạm thời không khả dụng",
                   StringComparison.OrdinalIgnoreCase) ||
               reason.Contains(
                   "Gemini trả JSON chưa hoàn chỉnh",
                   StringComparison.OrdinalIgnoreCase) ||
               reason.Contains(
                   "Gemini chưa trả được JSON",
                   StringComparison.OrdinalIgnoreCase) ||
               reason.Contains(
                   "Gemini planning chưa trả",
                   StringComparison.OrdinalIgnoreCase) ||
               reason.Contains(
                   "Gemini planning không trả",
                   StringComparison.OrdinalIgnoreCase);
    }

    public DesktopOperatorDecision CreateProviderUnavailableBlockedDecision() =>
        new(
            State: "Structured/local capability không đủ cho bước hiện tại và semantic provider không khả dụng.",
            Plan: "Không thực hiện hành động không chắc chắn. Giữ nguyên desktop và báo provider unavailable để có thể tiếp tục khi provider phục hồi.",
            CurrentSubgoal: string.Empty,
            GoalProgress: 0,
            VerifiedMilestones: Array.Empty<string>(),
            Action: "blocked",
            Query: string.Empty,
            Text: string.Empty,
            Key: string.Empty,
            Keys: Array.Empty<string>(),
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
            ExpectedEffect: string.Empty,
            Confidence: 1.0,
            Reason: "Gemini/Vision hiện không khả dụng và structured/local planner không có action đủ chắc chắn. Đây là provider unavailable, không phải lỗi của local Computer Operator.",
            SceneElements: Array.Empty<DesktopSceneElement>(),
            TargetElementId: string.Empty);

    public DesktopOperatorDecision CreatePlannerCooldownWaitDecision(
        DateTimeOffset backoffUntil) =>
        new(
            State: "Provider semantic đang cooldown; scene hiện tại chưa đổi.",
            Plan: "Không gọi lại provider trên cùng scene. Chờ ngắn hoặc dùng local capability nếu có.",
            CurrentSubgoal: string.Empty,
            GoalProgress: 0,
            VerifiedMilestones: Array.Empty<string>(),
            Action: "wait",
            Query: string.Empty,
            Text: string.Empty,
            Key: string.Empty,
            Keys: Array.Empty<string>(),
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
            ExpectedEffect: string.Empty,
            Confidence: 1.0,
            Reason: $"Gemini đang cooldown đến {backoffUntil:HH:mm:ss}; không gọi lặp khi scene chưa thay đổi.",
            SceneElements: Array.Empty<DesktopSceneElement>(),
            TargetElementId: string.Empty);
}
