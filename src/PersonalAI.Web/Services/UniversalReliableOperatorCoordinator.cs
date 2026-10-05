namespace PersonalAI.Web.Services;

public sealed record UniversalReliableOperatorRuntimeSnapshot(
    string Version,
    bool Ready,
    IReadOnlyList<string> AvailableComputerCapabilities,
    IReadOnlyList<UniversalResilienceDomainStatus> ResilienceDomains,
    UniversalCapabilityCacheStatus CapabilityCache,
    string Reason);

public sealed record UniversalReliableVerificationDecision(
    bool Verified,
    bool RequiresSemanticVerification,
    double Confidence,
    string Source,
    string Reason,
    IReadOnlyList<UniversalEvidenceSignal> Evidence);

public interface IUniversalReliableOperatorCoordinator
{
    UniversalReliableOperatorRuntimeSnapshot GetRuntimeSnapshot();

    UniversalReliableVerificationDecision EvaluateLocalVerification(
        DesktopOperatorDecision decision,
        DesktopVerificationRoutingResult route);

    UniversalReliableVerificationDecision EvaluateSemanticVerification(
        DesktopOperatorDecision decision,
        DesktopVerificationRoutingResult? localRoute,
        bool semanticPassed,
        double semanticConfidence,
        string semanticReason);

    UniversalFailureAssessment ClassifyFailure(
        string? detail,
        bool sideEffectMayHaveOccurred);
}

public sealed class UniversalReliableOperatorCoordinator(
    IUniversalCapabilityDiscoveryService capabilities,
    IUniversalCapabilityCache capabilityCache,
    IUniversalEvidenceFusionEngine evidenceFusion,
    IUniversalResilienceExecutor resilience)
    : IUniversalReliableOperatorCoordinator
{
    public UniversalReliableOperatorRuntimeSnapshot GetRuntimeSnapshot()
    {
        var snapshot =
            capabilities.Discover();

        var availableComputer =
            snapshot.ForChannel(
                    ExecutionAgentChannels.Computer)
                .Where(item => item.Available)
                .OrderBy(item => item.Priority)
                .Select(item => item.Key)
                .ToArray();

        return new(
            snapshot.Version,
            Ready: availableComputer.Length > 0,
            availableComputer,
            resilience.GetStatus(),
            capabilityCache.GetStatus(),
            availableComputer.Length > 0
                ? $"Có {availableComputer.Length} capability desktop khả dụng: {string.Join(", ", availableComputer)}."
                : "Không có capability desktop khả dụng; không được thực thi Computer Operator.");
    }

    public UniversalReliableVerificationDecision EvaluateLocalVerification(
        DesktopOperatorDecision decision,
        DesktopVerificationRoutingResult route)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(route);

        if (route.Route !=
            DesktopVerificationRoute.LocalVerified)
        {
            return new(
                Verified: false,
                RequiresSemanticVerification: true,
                route.Confidence,
                "local-not-verified",
                route.Reason,
                Array.Empty<UniversalEvidenceSignal>());
        }

        var signal =
            BuildLocalSignal(
                decision,
                route);

        var fused =
            evidenceFusion.Fuse(
                [signal]);

        return new(
            Verified:
                fused.Status ==
                UniversalEvidenceFusionStatuses.Verified,
            RequiresSemanticVerification:
                fused.Status ==
                UniversalEvidenceFusionStatuses.NeedsVerification,
            fused.Confidence,
            fused.Source,
            fused.Reason,
            fused.AcceptedSignals);
    }

    public UniversalReliableVerificationDecision EvaluateSemanticVerification(
        DesktopOperatorDecision decision,
        DesktopVerificationRoutingResult? localRoute,
        bool semanticPassed,
        double semanticConfidence,
        string semanticReason)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var signals =
            new List<UniversalEvidenceSignal>();

        if (localRoute?.Route ==
            DesktopVerificationRoute.LocalVerified)
        {
            signals.Add(
                BuildLocalSignal(
                    decision,
                    localRoute));
        }

        signals.Add(
            new(
                Source: "gemini-vision",
                SourceGroup: "semantic-vision",
                Passed: semanticPassed,
                Confidence: Math.Clamp(
                    semanticConfidence,
                    0,
                    1),
                Reliability:
                    UniversalEvidenceReliability.Semantic,
                Summary:
                    string.IsNullOrWhiteSpace(
                        semanticReason)
                        ? "Gemini semantic verification."
                        : semanticReason.Trim()));

        var fused =
            evidenceFusion.Fuse(
                signals);

        if (fused.Status ==
            UniversalEvidenceFusionStatuses.Verified)
        {
            return new(
                Verified: true,
                RequiresSemanticVerification: false,
                fused.Confidence,
                fused.Source,
                fused.Reason,
                fused.AcceptedSignals);
        }

        if (fused.Status ==
            UniversalEvidenceFusionStatuses.NotAchieved)
        {
            return new(
                Verified: false,
                RequiresSemanticVerification: false,
                fused.Confidence,
                fused.Source,
                fused.Reason,
                fused.AcceptedSignals);
        }

        // Visual-only/custom-rendered UI có thể không có structured evidence.
        // Cho semantic fallback kết luận chỉ khi confidence rất cao và không có
        // local evidence mạnh xung đột. Kết quả này không được gắn nhãn
        // independently verified.
        var hasLocalStrong =
            signals.Any(item =>
                item.SourceGroup !=
                    "semantic-vision" &&
                item.Reliability is
                    UniversalEvidenceReliability.Deterministic or
                    UniversalEvidenceReliability.Structured or
                    UniversalEvidenceReliability.StrongLocal);

        if (!hasLocalStrong &&
            semanticPassed &&
            semanticConfidence >= 0.92)
        {
            return new(
                Verified: true,
                RequiresSemanticVerification: false,
                Math.Clamp(
                    semanticConfidence,
                    0,
                    1),
                "semantic-fallback",
                "Không có structured/local evidence độc lập; chấp nhận Gemini Vision fallback với confidence rất cao (>=0.92).",
                signals);
        }

        return new(
            Verified: false,
            RequiresSemanticVerification: false,
            fused.Confidence,
            fused.Source,
            fused.Reason,
            fused.AcceptedSignals);
    }

    public UniversalFailureAssessment ClassifyFailure(
        string? detail,
        bool sideEffectMayHaveOccurred) =>
        resilience.Classify(
            exception: null,
            detail,
            sideEffectMayHaveOccurred);

    private static UniversalEvidenceSignal BuildLocalSignal(
        DesktopOperatorDecision decision,
        DesktopVerificationRoutingResult route)
    {
        var action =
            (decision.Action ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        var deterministic =
            action is
                "focus-window" or
                "maximize" or
                "restore" or
                "minimize";

        return new(
            Source:
                deterministic
                    ? "action-specific-window-verifier"
                    : "action-specific-local-verifier",
            SourceGroup:
                deterministic
                    ? "window-state"
                    : "local-action-state",
            Passed: true,
            Confidence: Math.Clamp(
                route.Confidence,
                0,
                1),
            Reliability:
                deterministic
                    ? UniversalEvidenceReliability.Deterministic
                    : UniversalEvidenceReliability.StrongLocal,
            Summary: route.Reason);
    }
}
