using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ComputerLocalVerificationResult(
    bool Handled,
    bool Verified,
    bool SemanticVerified,
    double Confidence,
    string Detail,
    string EvidenceKind);

public interface IComputerOperatorLocalVerificationService
{
    ComputerLocalVerificationResult Verify(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame? before,
        DesktopScreenshotFrame after,
        DesktopFrameDifference? difference);
}

public sealed class ComputerOperatorLocalVerificationService(
    IComputerUseService computer)
    : IComputerOperatorLocalVerificationService
{
    private const double MinimumChangedRatio = 0.0005;
    private const int MinimumChangedSamples = 12;

    public ComputerLocalVerificationResult Verify(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame? before,
        DesktopScreenshotFrame after,
        DesktopFrameDifference? difference)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(after);

        var action = (decision.Action ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (action == "focus-window")
            return VerifyForeground(decision);

        if (action is "wait" or "complete" or "blocked" or "move-pointer")
            return Inconclusive("Hành động này không dùng bộ xác minh local nhanh.");

        var windowChanged =
            before is not null &&
            !string.IsNullOrWhiteSpace(before.WindowId) &&
            !string.IsNullOrWhiteSpace(after.WindowId) &&
            !string.Equals(
                before.WindowId,
                after.WindowId,
                StringComparison.OrdinalIgnoreCase);

        if (windowChanged)
        {
            return Provisional(
                0.98,
                "Local xác nhận cửa sổ foreground/capture đã thay đổi sau hành động. " +
                "Đây là bằng chứng hành động có hiệu lực; trạng thái mục tiêu cụ thể sẽ được Gemini đánh giá ở lượt quan sát kế tiếp.",
                "window-transition");
        }

        if (difference is not null &&
            difference.Comparable &&
            difference.HasMeaningfulChange &&
            (difference.ChangedRatio >= MinimumChangedRatio ||
             difference.ChangedPixelSamples >= MinimumChangedSamples))
        {
            return Provisional(
                0.94,
                $"Local phát hiện thay đổi hình ảnh đáng kể sau hành động: " +
                $"{difference.ChangedRatio * 100:0.00}% mẫu thay đổi, " +
                $"{difference.ChangedPixelSamples} mẫu vượt ngưỡng. " +
                "Không cần gọi Gemini chỉ để xác nhận hành động đã tạo hiệu ứng; " +
                "Gemini vẫn quan sát ảnh mới ở bước suy luận tiếp theo.",
                "frame-difference");
        }

        return Inconclusive(
            "Local chưa có bằng chứng đủ mạnh để xác minh hành động; cần Gemini kiểm tra ngữ nghĩa.");
    }

    private ComputerLocalVerificationResult VerifyForeground(
        DesktopOperatorDecision decision)
    {
        var query = (decision.Query ?? string.Empty).Trim();
        if (query.Length == 0)
            return Inconclusive("Thiếu query để kiểm tra cửa sổ foreground.");

        var active = computer.GetActiveWindow();
        if (active is null)
            return Inconclusive("Local chưa xác định được cửa sổ foreground.");

        var title = active.Title ?? string.Empty;
        var process = active.ProcessName ?? string.Empty;

        var matched =
            title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            process.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            query.Contains(title, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(process) &&
             query.Contains(process, StringComparison.OrdinalIgnoreCase));

        if (!matched)
            return Inconclusive(
                $"Foreground hiện tại là “{title}” ({process}); local chưa thể chứng minh nó khớp “{query}”.");

        return new(
            true,
            true,
            true,
            1.0,
            $"Local xác nhận cửa sổ foreground hiện tại khớp “{query}”: “{title}” ({process}).",
            "foreground-window");
    }

    private static ComputerLocalVerificationResult Provisional(
        double confidence,
        string detail,
        string evidenceKind) =>
        new(
            true,
            true,
            false,
            confidence,
            detail,
            evidenceKind);

    private static ComputerLocalVerificationResult Inconclusive(
        string detail) =>
        new(
            false,
            false,
            false,
            0.0,
            detail,
            "inconclusive");
}
