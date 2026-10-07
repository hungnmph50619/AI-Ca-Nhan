using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class ComputerOperatorVerificationStatuses
{
    public const string Verified = "verified";
    public const string Failed = "failed";
    public const string Wait = "wait";
    public const string SemanticRequired = "semantic-required";
}

public sealed record ComputerOperatorLocalVerificationAssessment(
    string Status,
    DesktopVerificationRoutingResult Route,
    AdaptiveGeminiCallDecision AdaptiveDecision,
    UniversalReliableVerificationDecision? LocalFusion,
    bool ShouldAdaptiveWait,
    double Confidence,
    string Reason);

public sealed record ComputerOperatorAuthoritativeVerificationAssessment(
    bool Handled,
    bool Verified,
    double Confidence,
    string Detail,
    string Route);

public interface IComputerOperatorVerificationEngine
{
    ComputerOperatorAuthoritativeVerificationAssessment EvaluateAuthoritativeText(
        TextInteractionResult? textInteraction);

    ComputerOperatorLocalVerificationAssessment EvaluateLocal(
        DesktopOperatorDecision decision,
        DesktopFastObservation observation,
        DesktopFrameDifference? frameDifference,
        LocalVisualVerificationResult visualVerdict);

    UniversalReliableVerificationDecision EvaluateSemantic(
        DesktopOperatorDecision decision,
        DesktopVerificationRoutingResult? localRoute,
        bool semanticPassed,
        double semanticConfidence,
        string semanticReason);
}

/// <summary>
/// Authority duy nhất của pha verification policy.
/// Sensor/router/fusion chỉ cung cấp evidence; engine này quyết định
/// VERIFIED / FAILED / WAIT / SEMANTIC_REQUIRED.
/// </summary>
public sealed class ComputerOperatorVerificationEngine(
    IDesktopVerificationRouter router,
    IAdaptiveGeminiCallPolicy geminiPolicy,
    IUniversalReliableOperatorCoordinator reliableOperator)
    : IComputerOperatorVerificationEngine
{
    public ComputerOperatorAuthoritativeVerificationAssessment EvaluateAuthoritativeText(
        TextInteractionResult? textInteraction)
    {
        if (textInteraction is not
            {
                Verified: true,
                ActualText: not null
            })
        {
            return new(
                Handled: false,
                Verified: false,
                Confidence: 0,
                Detail: "Không có authoritative local text evidence.",
                Route: "not-applicable");
        }

        return new(
            Handled: true,
            Verified: true,
            Confidence: 1.0,
            Detail: $"Local Text Verifier: {textInteraction.Detail}",
            Route: "local-text");
    }

    public ComputerOperatorLocalVerificationAssessment EvaluateLocal(
        DesktopOperatorDecision decision,
        DesktopFastObservation observation,
        DesktopFrameDifference? frameDifference,
        LocalVisualVerificationResult visualVerdict)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(visualVerdict);

        var route =
            router.Route(
                decision,
                observation,
                frameDifference);

        if (route.Route ==
                DesktopVerificationRoute.LocalVerified &&
            visualVerdict.Status ==
                LocalVisualVerificationStatus.Changed &&
            visualVerdict.Confidence >= 0.88)
        {
            route =
                route with
                {
                    Confidence =
                        Math.Min(
                            0.99,
                            Math.Max(
                                route.Confidence,
                                visualVerdict.Confidence)),
                    Reason =
                        $"{route.Reason} Local visual fusion corroborates change: {visualVerdict.Reason}"
                };
        }

        UniversalReliableVerificationDecision? localFusion =
            null;

        if (route.Route ==
            DesktopVerificationRoute.LocalVerified)
        {
            localFusion =
                reliableOperator.EvaluateLocalVerification(
                    decision,
                    route);

            if (localFusion.Verified)
            {
                return new(
                    ComputerOperatorVerificationStatuses.Verified,
                    route,
                    new AdaptiveGeminiCallDecision(
                        AdaptiveGeminiDecision.SkipAndPass,
                        localFusion.Confidence,
                        localFusion.Reason),
                    localFusion,
                    ShouldAdaptiveWait: false,
                    localFusion.Confidence,
                    localFusion.Reason);
            }
        }

        var shouldAdaptiveWait =
            ComputerOperatorAdaptiveWaitPolicy.SupportsAdaptiveWaiting(
                decision.Action) &&
            (route.Route == DesktopVerificationRoute.LocalFailed ||
             visualVerdict.Status ==
                LocalVisualVerificationStatus.Stable ||
             (route.Route == DesktopVerificationRoute.GeminiRequired &&
              !observation.ForegroundWindowChanged &&
              !observation.WindowBoundsChanged &&
              (frameDifference?.Comparable != true ||
               frameDifference.ChangedRatio < 0.01)));

        if (shouldAdaptiveWait)
        {
            return new(
                ComputerOperatorVerificationStatuses.Wait,
                route,
                new AdaptiveGeminiCallDecision(
                    AdaptiveGeminiDecision.CallGemini,
                    0,
                    "Chờ adaptive observation trước khi quyết định semantic verification."),
                localFusion,
                ShouldAdaptiveWait: true,
                Math.Max(
                    route.Confidence,
                    visualVerdict.Confidence),
                "Local evidence chưa đủ kết luận; cần adaptive wait rồi đánh giá lại.");
        }

        if (route.Route ==
            DesktopVerificationRoute.LocalFailed)
        {
            return new(
                ComputerOperatorVerificationStatuses.Failed,
                route,
                new AdaptiveGeminiCallDecision(
                    AdaptiveGeminiDecision.SkipAndFail,
                    route.Confidence,
                    route.Reason),
                localFusion,
                ShouldAdaptiveWait: false,
                route.Confidence,
                route.Reason);
        }

        var adaptive =
            geminiPolicy.EvaluateVerification(
                decision,
                observation,
                frameDifference);

        var status =
            adaptive.Decision switch
            {
                AdaptiveGeminiDecision.SkipAndPass =>
                    ComputerOperatorVerificationStatuses.Verified,
                AdaptiveGeminiDecision.SkipAndFail =>
                    ComputerOperatorVerificationStatuses.Failed,
                _ =>
                    ComputerOperatorVerificationStatuses.SemanticRequired
            };

        return new(
            status,
            route,
            adaptive,
            localFusion,
            ShouldAdaptiveWait: false,
            Math.Max(
                route.Confidence,
                adaptive.Confidence),
            adaptive.Reason);
    }

    public UniversalReliableVerificationDecision EvaluateSemantic(
        DesktopOperatorDecision decision,
        DesktopVerificationRoutingResult? localRoute,
        bool semanticPassed,
        double semanticConfidence,
        string semanticReason) =>
        reliableOperator.EvaluateSemanticVerification(
            decision,
            localRoute,
            semanticPassed,
            semanticConfidence,
            semanticReason);
}
