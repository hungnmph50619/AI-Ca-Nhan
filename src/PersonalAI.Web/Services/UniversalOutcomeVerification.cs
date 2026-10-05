using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class UniversalOutcomeStatuses
{
    public const string Verified = "verified";
    public const string NotAchieved = "not-achieved";
    public const string NeedsVerification = "needs-verification";
    public const string ExecutionFailed = "execution-failed";
}

public sealed record UniversalOutcomeEvidence(
    string Source,
    bool Passed,
    double Confidence,
    string Summary);

public sealed record UniversalToolOutcomeVerificationRequest(
    string Goal,
    ToolExecutionResponse Execution,
    UniversalOutcomeEvidence? Evidence = null,
    IReadOnlyList<UniversalEvidenceSignal>? EvidenceSignals = null);

public sealed record UniversalAgentOutcomeVerificationRequest(
    string Goal,
    ExecutionAgentResult Execution,
    UniversalOutcomeEvidence? Evidence = null,
    IReadOnlyList<UniversalEvidenceSignal>? EvidenceSignals = null);

public sealed record UniversalOutcomeVerificationResult(
    string Status,
    bool ExecutionSucceeded,
    bool GoalAchieved,
    bool IndependentlyVerified,
    double Confidence,
    string Source,
    string Reason);

public interface IUniversalOutcomeVerificationService
{
    UniversalOutcomeVerificationResult VerifyTool(
        UniversalToolOutcomeVerificationRequest request);

    UniversalOutcomeVerificationResult VerifyAgent(
        UniversalAgentOutcomeVerificationRequest request);
}

public sealed class UniversalOutcomeVerificationService(
    IToolCapabilityRegistry toolCapabilities,
    IExecutionAgentRegistry executionAgents,
    IUniversalVerificationEvidenceAdapters? evidenceAdapters = null,
    IUniversalEvidenceFusionEngine? evidenceFusion = null)
    : IUniversalOutcomeVerificationService
{
    private readonly IUniversalVerificationEvidenceAdapters evidenceAdapters =
        evidenceAdapters ?? new UniversalVerificationEvidenceAdapters();

    private readonly IUniversalEvidenceFusionEngine evidenceFusion =
        evidenceFusion ?? new UniversalEvidenceFusionEngine();

    private const double StrongEvidenceThreshold = 0.80;

    public UniversalOutcomeVerificationResult VerifyTool(
        UniversalToolOutcomeVerificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Execution);

        var executionSucceeded =
            request.Execution.Success &&
            request.Execution.Status.Equals(
                ToolExecutionStatuses.Succeeded,
                StringComparison.OrdinalIgnoreCase);

        if (!executionSucceeded)
        {
            return new(
                UniversalOutcomeStatuses.ExecutionFailed,
                ExecutionSucceeded: false,
                GoalAchieved: false,
                IndependentlyVerified: false,
                Confidence: 1.0,
                Source: "tool-execution",
                $"Tool execution chưa thành công: {request.Execution.Status}.");
        }

        toolCapabilities.TryGet(
            request.Execution.ToolName,
            out var capability);

        if (request.EvidenceSignals is { Count: > 0 })
        {
            var fused =
                evidenceFusion.Fuse(
                    request.EvidenceSignals);

            return FromFusion(
                fused,
                executionSucceeded: true);
        }

        var evidence = request.Evidence;

        if (evidence is null)
        {
            return new(
                UniversalOutcomeStatuses.NeedsVerification,
                ExecutionSucceeded: true,
                GoalAchieved: false,
                IndependentlyVerified: false,
                Confidence: 0,
                Source: capability?.SupportsVerification == true
                    ? "verification-evidence-missing"
                    : "tool-has-no-verifier",
                capability?.SupportsVerification == true
                    ? "Tool báo thành công nhưng chưa có outcome evidence từ verifier."
                    : "Tool báo thành công nhưng tool chưa có verifier độc lập; không được coi goal đã đạt.");
        }

        var confidence = Math.Clamp(
            evidence.Confidence,
            0,
            1);

        if (confidence < StrongEvidenceThreshold)
        {
            return new(
                UniversalOutcomeStatuses.NeedsVerification,
                ExecutionSucceeded: true,
                GoalAchieved: false,
                IndependentlyVerified: false,
                confidence,
                evidence.Source,
                $"Outcome evidence chưa đủ mạnh ({confidence:0.00} < {StrongEvidenceThreshold:0.00}): {evidence.Summary}");
        }

        if (!evidence.Passed)
        {
            return new(
                UniversalOutcomeStatuses.NotAchieved,
                ExecutionSucceeded: true,
                GoalAchieved: false,
                IndependentlyVerified: true,
                confidence,
                evidence.Source,
                $"Execution chạy xong nhưng verifier xác nhận goal chưa đạt: {evidence.Summary}");
        }

        return new(
            UniversalOutcomeStatuses.Verified,
            ExecutionSucceeded: true,
            GoalAchieved: true,
            IndependentlyVerified: true,
            confidence,
            evidence.Source,
            $"Verifier xác nhận goal đã đạt: {evidence.Summary}");
    }

    public UniversalOutcomeVerificationResult VerifyAgent(
        UniversalAgentOutcomeVerificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Execution);

        var executionSucceeded =
            request.Execution.Status.Equals(
                AgentExecutionStatuses.Succeeded,
                StringComparison.OrdinalIgnoreCase);

        if (!executionSucceeded)
        {
            return new(
                UniversalOutcomeStatuses.ExecutionFailed,
                ExecutionSucceeded: false,
                GoalAchieved: false,
                IndependentlyVerified: false,
                Confidence: 1.0,
                Source: request.Execution.AgentId,
                $"Execution agent chưa thành công: {request.Execution.Status}.");
        }

        var definition =
            executionAgents.GetAll()
                .FirstOrDefault(agent =>
                    agent.Id.Equals(
                        request.Execution.AgentId,
                        StringComparison.OrdinalIgnoreCase));

        if (definition?.SupportsVerification != true)
        {
            return new(
                UniversalOutcomeStatuses.NeedsVerification,
                ExecutionSucceeded: true,
                GoalAchieved: false,
                IndependentlyVerified: false,
                Confidence: 0,
                Source: request.Execution.AgentId,
                "Agent báo execution thành công nhưng agent contract không hỗ trợ verification.");
        }

        if (request.EvidenceSignals is { Count: > 0 })
        {
            var fused =
                evidenceFusion.Fuse(
                    request.EvidenceSignals);

            return FromFusion(
                fused,
                executionSucceeded: true);
        }

        var evidence =
            request.Evidence ??
            evidenceAdapters.FromAgent(
                request.Execution);

        if (evidence is null)
        {
            return new(
                UniversalOutcomeStatuses.NeedsVerification,
                ExecutionSucceeded: true,
                GoalAchieved: false,
                IndependentlyVerified: false,
                Confidence: 0,
                Source: request.Execution.AgentId,
                "Agent báo execution thành công nhưng adapter chưa tạo được universal outcome evidence.");
        }

        var confidence = Math.Clamp(
            evidence.Confidence,
            0,
            1);

        if (confidence < StrongEvidenceThreshold)
        {
            return new(
                UniversalOutcomeStatuses.NeedsVerification,
                ExecutionSucceeded: true,
                GoalAchieved: false,
                IndependentlyVerified: false,
                confidence,
                evidence.Source,
                $"Universal agent evidence chưa đủ mạnh ({confidence:0.00} < {StrongEvidenceThreshold:0.00}): {evidence.Summary}");
        }

        if (!evidence.Passed)
        {
            return new(
                UniversalOutcomeStatuses.NotAchieved,
                ExecutionSucceeded: true,
                GoalAchieved: false,
                IndependentlyVerified: true,
                confidence,
                evidence.Source,
                $"Agent execution thành công nhưng universal verifier xác nhận goal chưa đạt: {evidence.Summary}");
        }

        return new(
            UniversalOutcomeStatuses.Verified,
            ExecutionSucceeded: true,
            GoalAchieved: true,
            IndependentlyVerified: true,
            confidence,
            evidence.Source,
            $"Universal evidence xác nhận agent đã đạt goal: {evidence.Summary}");
    }

    private static UniversalOutcomeVerificationResult FromFusion(
        UniversalEvidenceFusionResult fused,
        bool executionSucceeded)
    {
        return fused.Status switch
        {
            UniversalEvidenceFusionStatuses.Verified =>
                new(
                    UniversalOutcomeStatuses.Verified,
                    executionSucceeded,
                    GoalAchieved: true,
                    IndependentlyVerified:
                        fused.IndependentlyVerified,
                    fused.Confidence,
                    fused.Source,
                    $"Hợp nhất bằng chứng xác nhận goal đã đạt: {fused.Reason}"),

            UniversalEvidenceFusionStatuses.NotAchieved =>
                new(
                    UniversalOutcomeStatuses.NotAchieved,
                    executionSucceeded,
                    GoalAchieved: false,
                    IndependentlyVerified:
                        fused.IndependentlyVerified,
                    fused.Confidence,
                    fused.Source,
                    $"Hợp nhất bằng chứng xác nhận goal chưa đạt: {fused.Reason}"),

            _ =>
                new(
                    UniversalOutcomeStatuses.NeedsVerification,
                    executionSucceeded,
                    GoalAchieved: false,
                    IndependentlyVerified: false,
                    fused.Confidence,
                    fused.Source,
                    $"Bằng chứng chưa đủ để kết luận: {fused.Reason}")
        };
    }
}
