namespace PersonalAI.Web.Services;

public static class ComputerOperatorFailureTaxonomy
{
    public const string TargetNotFound = "target-not-found";
    public const string TargetMoved = "target-moved";
    public const string TargetOccluded = "target-occluded";
    public const string WrongWindow = "wrong-window";
    public const string WrongFocus = "wrong-focus";
    public const string ActionNoEffect = "action-no-effect";
    public const string UnexpectedPopup = "unexpected-popup";
    public const string LoadingTimeout = "loading-timeout";
    public const string KeyboardRejected = "keyboard-rejected";
    public const string ScrollNoEffect = "scroll-no-effect";
    public const string WindowChanged = "window-changed";
    public const string ResolutionChanged = "resolution-changed";
    public const string LowConfidence = "low-confidence";
    public const string Unknown = "unknown";
}

public enum ComputerOperatorRecoveryAction
{
    Reobserve,
    Replan,
    Refocus,
    Retarget,
    WaitForStableUi,
    DismissBlockingSurface,
    RecalibrateCoordinates,
    GeminiInspect,
    Block
}

public sealed record ComputerOperatorFailureContext(
    string FailureKind,
    string Action,
    string Detail,
    bool TargetMissing = false,
    bool TargetMoved = false,
    bool TargetOccluded = false,
    bool ForegroundChanged = false,
    bool WindowBoundsChanged = false,
    bool MonitorChanged = false,
    bool DpiChanged = false,
    double ChangeRatio = 0,
    int RepeatedFailures = 1);

public sealed record ComputerOperatorRecoveryPlan(
    ComputerOperatorRecoveryAction PrimaryAction,
    IReadOnlyList<ComputerOperatorRecoveryAction> Fallbacks,
    bool AllowSameStrategyRetry,
    int DelayMs,
    string Reason);

public interface IComputerOperatorFailureRecoveryEngine
{
    ComputerOperatorRecoveryPlan Plan(
        ComputerOperatorFailureContext context);
}

public sealed class ComputerOperatorFailureRecoveryEngine
    : IComputerOperatorFailureRecoveryEngine
{
    public ComputerOperatorRecoveryPlan Plan(
        ComputerOperatorFailureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.RepeatedFailures >= 3)
        {
            return new(
                ComputerOperatorRecoveryAction.GeminiInspect,
                new[]
                {
                    ComputerOperatorRecoveryAction.Replan,
                    ComputerOperatorRecoveryAction.Block
                },
                false,
                350,
                "Cùng nhóm lỗi đã lặp nhiều lần; cấm retry y hệt và yêu cầu quan sát semantic trước khi quyết định tiếp.");
        }

        if (context.MonitorChanged ||
            context.DpiChanged)
        {
            return new(
                ComputerOperatorRecoveryAction.RecalibrateCoordinates,
                new[]
                {
                    ComputerOperatorRecoveryAction.Reobserve,
                    ComputerOperatorRecoveryAction.Replan
                },
                false,
                200,
                "Monitor hoặc DPI đã thay đổi; phải hiệu chỉnh lại interaction space trước khi thao tác tiếp.");
        }

        if (context.TargetOccluded)
        {
            return new(
                ComputerOperatorRecoveryAction.DismissBlockingSurface,
                new[]
                {
                    ComputerOperatorRecoveryAction.GeminiInspect,
                    ComputerOperatorRecoveryAction.Retarget,
                    ComputerOperatorRecoveryAction.Replan
                },
                false,
                250,
                "Target có dấu hiệu bị che; ưu tiên xử lý surface chắn thay vì click vào tọa độ cũ.");
        }

        if (context.TargetMissing)
        {
            return new(
                ComputerOperatorRecoveryAction.Retarget,
                new[]
                {
                    ComputerOperatorRecoveryAction.GeminiInspect,
                    ComputerOperatorRecoveryAction.Replan
                },
                false,
                200,
                "Target không còn xuất hiện; cần tìm target mới thay vì tái sử dụng bbox cũ.");
        }

        if (context.TargetMoved)
        {
            return new(
                ComputerOperatorRecoveryAction.Retarget,
                new[]
                {
                    ComputerOperatorRecoveryAction.Reobserve,
                    ComputerOperatorRecoveryAction.Replan
                },
                true,
                120,
                "Target đã dịch chuyển; cập nhật target geometry rồi mới cân nhắc thực hiện lại.");
        }

        if (context.ForegroundChanged)
        {
            return new(
                ComputerOperatorRecoveryAction.Refocus,
                new[]
                {
                    ComputerOperatorRecoveryAction.GeminiInspect,
                    ComputerOperatorRecoveryAction.Replan
                },
                false,
                180,
                "Foreground window thay đổi ngoài dự kiến; cần xác định/focus đúng cửa sổ trước khi tiếp tục.");
        }

        if (context.WindowBoundsChanged)
        {
            return new(
                ComputerOperatorRecoveryAction.Retarget,
                new[]
                {
                    ComputerOperatorRecoveryAction.Reobserve,
                    ComputerOperatorRecoveryAction.Replan
                },
                true,
                120,
                "Geometry cửa sổ thay đổi; phải remap target trước khi thao tác tiếp.");
        }

        var kind = (context.FailureKind ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        return kind switch
        {
            ComputerOperatorFailureTaxonomy.LoadingTimeout =>
                new(
                    ComputerOperatorRecoveryAction.WaitForStableUi,
                    new[]
                    {
                        ComputerOperatorRecoveryAction.Reobserve,
                        ComputerOperatorRecoveryAction.GeminiInspect
                    },
                    true,
                    700,
                    "UI có thể vẫn đang tải; chờ ổn định rồi quan sát lại trước khi re-plan."),

            ComputerOperatorFailureTaxonomy.ScrollNoEffect =>
                new(
                    ComputerOperatorRecoveryAction.Reobserve,
                    new[]
                    {
                        ComputerOperatorRecoveryAction.Retarget,
                        ComputerOperatorRecoveryAction.Replan
                    },
                    false,
                    180,
                    "Scroll không tạo thay đổi; cần kiểm tra focus/scroll container thay vì lặp lại cùng delta."),

            ComputerOperatorFailureTaxonomy.KeyboardRejected =>
                new(
                    ComputerOperatorRecoveryAction.Refocus,
                    new[]
                    {
                        ComputerOperatorRecoveryAction.Reobserve,
                        ComputerOperatorRecoveryAction.Replan
                    },
                    false,
                    180,
                    "Bàn phím không tạo hiệu ứng mong đợi; ưu tiên xác minh focus trước khi nhập lại."),

            ComputerOperatorFailureTaxonomy.ActionNoEffect =>
                new(
                    ComputerOperatorRecoveryAction.Reobserve,
                    new[]
                    {
                        ComputerOperatorRecoveryAction.GeminiInspect,
                        ComputerOperatorRecoveryAction.Replan
                    },
                    false,
                    220,
                    "Action không tạo hiệu ứng; không được retry y hệt nếu trạng thái không đổi."),

            ComputerOperatorFailureTaxonomy.UnexpectedPopup =>
                new(
                    ComputerOperatorRecoveryAction.GeminiInspect,
                    new[]
                    {
                        ComputerOperatorRecoveryAction.DismissBlockingSurface,
                        ComputerOperatorRecoveryAction.Replan
                    },
                    false,
                    150,
                    "Xuất hiện surface/popup ngoài dự kiến; cần hiểu ngữ nghĩa trước khi tương tác."),

            ComputerOperatorFailureTaxonomy.WrongWindow or
            ComputerOperatorFailureTaxonomy.WrongFocus =>
                new(
                    ComputerOperatorRecoveryAction.Refocus,
                    new[]
                    {
                        ComputerOperatorRecoveryAction.Reobserve,
                        ComputerOperatorRecoveryAction.Replan
                    },
                    false,
                    150,
                    "Sai cửa sổ/focus; phải khôi phục context trước khi tiếp tục."),

            ComputerOperatorFailureTaxonomy.ResolutionChanged or
            ComputerOperatorFailureTaxonomy.WindowChanged =>
                new(
                    ComputerOperatorRecoveryAction.RecalibrateCoordinates,
                    new[]
                    {
                        ComputerOperatorRecoveryAction.Reobserve,
                        ComputerOperatorRecoveryAction.Replan
                    },
                    false,
                    200,
                    "Không gian tương tác đã thay đổi; cần hiệu chỉnh geometry trước khi action tiếp theo."),

            _ =>
                new(
                    ComputerOperatorRecoveryAction.Replan,
                    new[]
                    {
                        ComputerOperatorRecoveryAction.Reobserve,
                        ComputerOperatorRecoveryAction.GeminiInspect
                    },
                    false,
                    250,
                    "Không đủ tín hiệu để recovery cục bộ; yêu cầu re-plan an toàn.")
        };
    }
}
