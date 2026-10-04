using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class UniversalVerificationStrategies
{
    public const string None = "none";
    public const string LocalToolVerifier = "local-tool-verifier";
    public const string BrowserReadback = "browser-readback";
    public const string CodingVerificationGate = "coding-verification-gate";
    public const string ConnectorReadback = "connector-readback";
    public const string ComputerOperatorVerifier = "computer-operator-verifier";
    public const string SemanticVerifier = "semantic-verifier";
}

public sealed record UniversalVerificationEvidenceRouteRequest(
    UniversalTaskRoutePreview Route,
    ToolExecutionResponse Execution);

public sealed record UniversalVerificationEvidenceRoute(
    string Strategy,
    string Channel,
    bool CanVerifyAutomatically,
    bool RequiresExternalAi,
    bool RequiresReadback,
    string ExpectedSource,
    string Reason);

public interface IUniversalVerificationEvidenceRouter
{
    UniversalVerificationEvidenceRoute Route(
        UniversalVerificationEvidenceRouteRequest request);
}

public sealed class UniversalVerificationEvidenceRouter(
    IToolCapabilityRegistry toolCapabilities)
    : IUniversalVerificationEvidenceRouter
{
    public UniversalVerificationEvidenceRoute Route(
        UniversalVerificationEvidenceRouteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Route);
        ArgumentNullException.ThrowIfNull(request.Execution);

        var channel =
            (request.Route.SelectedChannel ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (!request.Execution.Success ||
            !request.Execution.Status.Equals(
                ToolExecutionStatuses.Succeeded,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                UniversalVerificationStrategies.None,
                channel,
                CanVerifyAutomatically: false,
                RequiresExternalAi: false,
                RequiresReadback: false,
                ExpectedSource: "execution-status",
                $"Execution chưa thành công ({request.Execution.Status}); chưa route outcome verifier.");
        }

        toolCapabilities.TryGet(
            request.Execution.ToolName,
            out var capability);

        if (capability?.SupportsVerification == true)
        {
            return new(
                UniversalVerificationStrategies.LocalToolVerifier,
                channel,
                CanVerifyAutomatically: true,
                RequiresExternalAi: false,
                RequiresReadback: false,
                ExpectedSource: $"{request.Execution.ToolName}:verifier",
                "Tool khai báo SupportsVerification; ưu tiên verifier riêng của tool trước mọi semantic fallback.");
        }

        return channel switch
        {
            ExecutionAgentChannels.Browser =>
                new(
                    UniversalVerificationStrategies.BrowserReadback,
                    channel,
                    CanVerifyAutomatically: true,
                    RequiresExternalAi: false,
                    RequiresReadback: true,
                    ExpectedSource: "browser-structured-readback",
                    "Browser outcome nên xác minh bằng structured/readback state trước khi dùng vision hoặc LLM."),

            ExecutionAgentChannels.Coding =>
                new(
                    UniversalVerificationStrategies.CodingVerificationGate,
                    channel,
                    CanVerifyAutomatically: true,
                    RequiresExternalAi: false,
                    RequiresReadback: true,
                    ExpectedSource: "coding-verification-gate",
                    "Coding outcome phải qua restore/build/test/diff verification gate."),

            ExecutionAgentChannels.Connector =>
                new(
                    UniversalVerificationStrategies.ConnectorReadback,
                    channel,
                    CanVerifyAutomatically: true,
                    RequiresExternalAi: false,
                    RequiresReadback: true,
                    ExpectedSource: "connector-readback",
                    "Connector side effect cần readback từ nguồn đích thay vì tin response ban đầu."),

            ExecutionAgentChannels.Computer =>
                new(
                    UniversalVerificationStrategies.ComputerOperatorVerifier,
                    channel,
                    CanVerifyAutomatically: true,
                    RequiresExternalAi: true,
                    RequiresReadback: true,
                    ExpectedSource: "computer-operator-observe-verify",
                    "Desktop outcome phải quan sát lại màn hình; ưu tiên local verifier và chỉ dùng Vision khi semantic cần thiết."),

            _ =>
                new(
                    UniversalVerificationStrategies.SemanticVerifier,
                    channel,
                    CanVerifyAutomatically: false,
                    RequiresExternalAi: true,
                    RequiresReadback: true,
                    ExpectedSource: "semantic-outcome-verifier",
                    "Không có verifier cấu trúc cho channel hiện tại; cần semantic verification trước khi complete.")
        };
    }
}
