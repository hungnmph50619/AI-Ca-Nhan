using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class ComputerOperatorGroundingStatuses
{
    public const string Resolved = "resolved";
    public const string Ambiguous = "ambiguous";
    public const string Unavailable = "unavailable";
    public const string Stale = "stale";
}

public sealed record ComputerOperatorGroundingEvidence(
    string Source,
    bool SupportsTarget,
    double Confidence,
    string Detail);

public sealed record ComputerOperatorGroundingResult(
    string Status,
    DesktopOperatorDecision Decision,
    double Confidence,
    IReadOnlyList<ComputerOperatorGroundingEvidence> Evidence,
    string Reason)
{
    public bool Resolved =>
        Status.Equals(
            ComputerOperatorGroundingStatuses.Resolved,
            StringComparison.OrdinalIgnoreCase);
}

public interface IComputerOperatorGroundingService
{
    ComputerOperatorGroundingResult GroundClickTarget(
        DesktopOperatorDecision proposed,
        ComputerOperatorDesktopState state,
        bool deterministicTextSurface);
}

/// <summary>
/// Hợp nhất grounding evidence thành một contract duy nhất.
/// OCR/UIA/Vision/geometry chỉ cung cấp evidence; service này không execute action
/// và không thay đổi goal. Quyền EXECUTE/REPLAN vẫn thuộc flow điều khiển chính.
/// </summary>
public sealed class ComputerOperatorGroundingService(
    IDesktopOcrActionPlanner ocrActionPlanner)
    : IComputerOperatorGroundingService
{
    private const double MinimumPlannerVisualConfidence = 0.84;

    public ComputerOperatorGroundingResult GroundClickTarget(
        DesktopOperatorDecision proposed,
        ComputerOperatorDesktopState state,
        bool deterministicTextSurface)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(state);

        var evidence =
            new List<ComputerOperatorGroundingEvidence>();

        if (proposed.Action is not
            ("click-left" or "double-click-left" or "click-right"))
        {
            return Unavailable(
                proposed,
                evidence,
                "Grounding contract hiện chỉ áp dụng cho click target.");
        }

        if (string.IsNullOrWhiteSpace(proposed.TargetLabel))
        {
            if (HasUsablePlannerBox(proposed))
            {
                evidence.Add(
                    new(
                        "planner-visual",
                        true,
                        proposed.Confidence,
                        "Planner cung cấp bbox hợp lệ nhưng không có semantic label."));

                return Resolved(
                    proposed,
                    proposed.Confidence,
                    evidence,
                    "Planner visual bbox hợp lệ; chuyển sang geometry/safety validation.");
            }

            return Unavailable(
                proposed,
                evidence,
                "Không có semantic label hoặc bbox hợp lệ để grounding.");
        }

        if (ocrActionPlanner.TryGroundTarget(
                proposed,
                state,
                out var grounded,
                out var ocrReason))
        {
            evidence.Add(
                new(
                    "ocr-local",
                    true,
                    grounded.Confidence,
                    ocrReason));

            if (HasUsablePlannerBox(proposed))
            {
                evidence.Add(
                    new(
                        "planner-visual",
                        true,
                        proposed.Confidence,
                        "Planner cũng cung cấp visual bbox cho cùng semantic target."));
            }

            return Resolved(
                grounded,
                grounded.Confidence,
                evidence,
                "OCR local xác minh target duy nhất; dùng bbox local làm grounding chính.");
        }

        evidence.Add(
            new(
                "ocr-local",
                false,
                0,
                ocrReason));

        if (deterministicTextSurface)
        {
            return Unavailable(
                proposed,
                evidence,
                "System text surface yêu cầu deterministic grounding; OCR chưa xác minh được target duy nhất nên không dùng planner coordinates.");
        }

        if (CanUsePlannerVisualFallback(
                proposed,
                deterministicTextSurface))
        {
            evidence.Add(
                new(
                    "planner-visual",
                    true,
                    proposed.Confidence,
                    "Planner bbox hợp lệ và confidence đủ cao; OCR unavailable không có quyền phủ quyết nguồn evidence độc lập này."));

            return Resolved(
                proposed,
                proposed.Confidence,
                evidence,
                "OCR chưa resolve nhưng planner visual evidence đủ điều kiện; tiếp tục sang target tracking/safety.");
        }

        return Unavailable(
            proposed,
            evidence,
            "Không có grounding evidence đủ mạnh để tiếp tục.");
    }

    internal static bool CanUsePlannerVisualFallbackForAcceptance(
        DesktopOperatorDecision decision,
        bool deterministicTextSurface) =>
        CanUsePlannerVisualFallback(
            decision,
            deterministicTextSurface);

    private static bool CanUsePlannerVisualFallback(
        DesktopOperatorDecision decision,
        bool deterministicTextSurface) =>
        !deterministicTextSurface &&
        HasUsablePlannerBox(decision) &&
        double.IsFinite(decision.Confidence) &&
        decision.Confidence >= MinimumPlannerVisualConfidence;

    private static bool HasUsablePlannerBox(
        DesktopOperatorDecision decision) =>
        decision.BoxLeft >= 0 &&
        decision.BoxTop >= 0 &&
        decision.BoxWidth > 1 &&
        decision.BoxHeight > 1;

    private static ComputerOperatorGroundingResult Resolved(
        DesktopOperatorDecision decision,
        double confidence,
        IReadOnlyList<ComputerOperatorGroundingEvidence> evidence,
        string reason) =>
        new(
            ComputerOperatorGroundingStatuses.Resolved,
            decision,
            Math.Clamp(confidence, 0, 1),
            evidence,
            reason);

    private static ComputerOperatorGroundingResult Unavailable(
        DesktopOperatorDecision decision,
        IReadOnlyList<ComputerOperatorGroundingEvidence> evidence,
        string reason) =>
        new(
            ComputerOperatorGroundingStatuses.Unavailable,
            decision,
            0,
            evidence,
            reason);
}
