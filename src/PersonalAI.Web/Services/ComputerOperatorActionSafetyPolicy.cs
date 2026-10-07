using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorActionSafetyResult(
    bool Safe,
    string Code,
    string Reason);

public interface IComputerOperatorActionSafetyPolicy
{
    ComputerOperatorActionSafetyResult ValidateExpectedEffect(
        DesktopOperatorDecision decision);
}

/// <summary>
/// Chính sách an toàn trước EXECUTE cho action candidate.
/// Chỉ kiểm tra điều kiện bắt buộc để hành động có thể được VERIFY sau khi thực thi.
/// Không chọn action, không EXECUTE và không REPLAN.
/// </summary>
public sealed class ComputerOperatorActionSafetyPolicy
    : IComputerOperatorActionSafetyPolicy
{
    public ComputerOperatorActionSafetyResult ValidateExpectedEffect(
        DesktopOperatorDecision decision)
    {
        ArgumentNullException.ThrowIfNull(
            decision);

        if (!RequiresExpectedEffect(
                decision.Action))
        {
            return new(
                Safe: true,
                Code: "not-required",
                Reason: "Action này không bắt buộc khai báo expected effect.");
        }

        if (!string.IsNullOrWhiteSpace(
                decision.ExpectedEffect))
        {
            return new(
                Safe: true,
                Code: "present",
                Reason: "Action đã có expected effect để VERIFY sau EXECUTE.");
        }

        return new(
            Safe: false,
            Code: "missing-expected-effect",
            Reason: "Thiếu kết quả mong đợi để xác minh hành động.");
    }

    private static bool RequiresExpectedEffect(
        string action) =>
        action is not
            "wait" and not
            "complete" and not
            "blocked" and not
            "move-pointer";
}
