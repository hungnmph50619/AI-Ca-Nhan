namespace PersonalAI.Web.Services;

public sealed record UniversalVerificationContinuationPlan(
    string Strategy,
    string Channel,
    bool Required,
    bool RequiresReadback,
    bool RequiresExternalAi,
    string Reason);

public interface IUniversalVerificationContinuationPlanner
{
    UniversalVerificationContinuationPlan Plan(
        UniversalTaskRoutePreview route,
        UniversalOutcomeVerificationResult verification);
}

public sealed class UniversalVerificationContinuationPlanner
    : IUniversalVerificationContinuationPlanner
{
    public UniversalVerificationContinuationPlan Plan(
        UniversalTaskRoutePreview route,
        UniversalOutcomeVerificationResult verification)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(verification);

        var channel =
            (route.SelectedChannel ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (!verification.Status.Equals(
                UniversalOutcomeStatuses.NeedsVerification,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                UniversalVerificationStrategies.None,
                channel,
                Required: false,
                RequiresReadback: false,
                RequiresExternalAi: false,
                $"Không cần continuation verification vì status={verification.Status}.");
        }

        return channel switch
        {
            ExecutionAgentChannels.Browser =>
                new(
                    UniversalVerificationStrategies.BrowserReadback,
                    channel,
                    Required: true,
                    RequiresReadback: true,
                    RequiresExternalAi: false,
                    "Tiếp tục bằng browser structured readback; không dùng LLM nếu trạng thái trang có thể đọc trực tiếp."),

            ExecutionAgentChannels.Coding =>
                new(
                    UniversalVerificationStrategies.CodingVerificationGate,
                    channel,
                    Required: true,
                    RequiresReadback: true,
                    RequiresExternalAi: false,
                    "Tiếp tục bằng coding verification gate: restore, build, test và diff review."),

            ExecutionAgentChannels.Connector =>
                new(
                    UniversalVerificationStrategies.ConnectorReadback,
                    channel,
                    Required: true,
                    RequiresReadback: true,
                    RequiresExternalAi: false,
                    "Tiếp tục bằng readback từ hệ thống đích để xác minh side effect thực sự đã xảy ra."),

            ExecutionAgentChannels.Computer =>
                new(
                    UniversalVerificationStrategies.ComputerOperatorVerifier,
                    channel,
                    Required: true,
                    RequiresReadback: true,
                    RequiresExternalAi: true,
                    "Tiếp tục bằng observe-verify trên desktop; ưu tiên local verifier, chỉ dùng Gemini Vision khi semantic cần thiết."),

            _ =>
                new(
                    UniversalVerificationStrategies.SemanticVerifier,
                    channel,
                    Required: true,
                    RequiresReadback: true,
                    RequiresExternalAi: true,
                    "Không có verifier cấu trúc cho channel; cần semantic verification trước khi complete.")
        };
    }
}
