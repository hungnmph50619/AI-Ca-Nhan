namespace PersonalAI.Web.Services;

public static class IntegrationStrategies
{
    public const string Own = "own";
    public const string Reuse = "reuse";
    public const string Adapt = "adapt";
    public const string Observe = "observe";
}

public sealed record ExternalIntegrationDecision(
    string Id,
    string Name,
    string Strategy,
    string Responsibility,
    string Boundary,
    IReadOnlyList<string> Contracts,
    bool Replaceable);

public interface IExternalIntegrationArchitecture
{
    IReadOnlyList<ExternalIntegrationDecision> GetAll();

    ExternalIntegrationDecision? Get(
        string id);
}

public sealed class ExternalIntegrationArchitecture
    : IExternalIntegrationArchitecture
{
    private static readonly IReadOnlyList<ExternalIntegrationDecision> Decisions =
    [
        new(
            "ai-ca-nhan.computer-operator",
            "AI-Ca-Nhan Computer Operator",
            IntegrationStrategies.Own,
            "Quan sát desktop, target tracking, mouse/keyboard execution, verify-after-action, recovery và anti-loop.",
            "Không giao quyền điều khiển desktop tổng quát cho framework ngoài.",
            [
                "IExecutionAgent",
                "IDesktopVerificationRouter",
                "IComputerOperatorConfidenceEngine",
                "IComputerOperatorFailureRecoveryEngine"
            ],
            Replaceable: false),

        new(
            "microsoft.agent-framework",
            "Microsoft Agent Framework",
            IntegrationStrategies.Adapt,
            "Agent runtime/orchestration và function-tool interoperability.",
            "Chỉ đi qua MicrosoftAgentFrameworkAdapter; side effect bắt buộc approval wrapper.",
            [
                "IMicrosoftAgentFrameworkAdapter",
                "IExecutionAgent"
            ],
            Replaceable: true),

        new(
            "openhands.agent-server",
            "OpenHands Agent Server",
            IntegrationStrategies.Adapt,
            "External coding engine cho tác vụ chỉnh sửa repository.",
            "Chỉ đi qua ICodingExecutionBackend; mặc định tắt; side effect chạy ở confirmation mode; kết quả phải qua Coding Verification Gate.",
            [
                "ICodingExecutionBackend",
                "IOpenHandsAgentServerClient",
                "ICodingVerificationGate"
            ],
            Replaceable: true),

        new(
            "ai-ca-nhan.browser",
            "AI-Ca-Nhan Browser Backend",
            IntegrationStrategies.Reuse,
            "HTTP/HTML browser execution backend an toàn hiện có.",
            "BrowserExecutionAgent chỉ phụ thuộc IBrowserExecutionBackend để có thể thay bằng Playwright/browser-use sau này.",
            [
                "IBrowserExecutionBackend",
                "IExecutionAgent"
            ],
            Replaceable: true),

        new(
            "external.openclaw",
            "OpenClaw-compatible integrations",
            IntegrationStrategies.Observe,
            "Nguồn ý tưởng cho gateway/connectors/automation, chưa phải dependency lõi.",
            "Không đưa OpenClaw vào Computer Operator core hoặc agent contracts.",
            [
                "IExecutionAgent",
                "IToolCapabilityRegistry"
            ],
            Replaceable: true)
    ];

    public IReadOnlyList<ExternalIntegrationDecision> GetAll() =>
        Decisions;

    public ExternalIntegrationDecision? Get(
        string id) =>
        Decisions.FirstOrDefault(item =>
            item.Id.Equals(
                (id ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase));
}
