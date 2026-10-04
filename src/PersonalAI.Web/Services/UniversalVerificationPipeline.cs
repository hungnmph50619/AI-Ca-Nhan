using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record UniversalVerificationPipelineRequest(
    string Goal,
    UniversalTaskRoutePreview Route,
    ToolExecutionResponse Execution,
    BrowserExecutionBackendResult? Browser = null,
    CodingVerificationReport? Coding = null,
    DesktopVerificationRoutingResult? Desktop = null,
    ExecutionAgentResult? Agent = null,
    UniversalOutcomeEvidence? ExplicitEvidence = null);

public sealed record UniversalVerificationPipelineResult(
    UniversalVerificationEvidenceRoute EvidenceRoute,
    UniversalOutcomeEvidence? Evidence,
    UniversalOutcomeVerificationResult Verification,
    UniversalFallbackDecision Decision);

public interface IUniversalVerificationPipeline
{
    UniversalVerificationPipelineResult Verify(
        UniversalVerificationPipelineRequest request);
}

public sealed class UniversalVerificationPipeline(
    IUniversalVerificationEvidenceRouter evidenceRouter,
    IUniversalVerificationEvidenceAdapters adapters,
    IUniversalOutcomeVerificationService outcomeVerification,
    IUniversalFallbackPolicy fallbackPolicy)
    : IUniversalVerificationPipeline
{
    public UniversalVerificationPipelineResult Verify(
        UniversalVerificationPipelineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Route);
        ArgumentNullException.ThrowIfNull(request.Execution);

        var evidenceRoute =
            evidenceRouter.Route(
                new UniversalVerificationEvidenceRouteRequest(
                    request.Route,
                    request.Execution));

        var evidence =
            request.ExplicitEvidence ??
            ResolveEvidence(
                evidenceRoute,
                request);

        var verification =
            outcomeVerification.VerifyTool(
                new UniversalToolOutcomeVerificationRequest(
                    request.Goal,
                    request.Execution,
                    evidence));

        var decision =
            fallbackPolicy.Evaluate(
                request.Route,
                request.Execution,
                verification);

        return new(
            evidenceRoute,
            evidence,
            verification,
            decision);
    }

    private UniversalOutcomeEvidence? ResolveEvidence(
        UniversalVerificationEvidenceRoute route,
        UniversalVerificationPipelineRequest request)
    {
        return route.Strategy switch
        {
            UniversalVerificationStrategies.None =>
                null,

            UniversalVerificationStrategies.LocalToolVerifier =>
                request.Agent is not null
                    ? adapters.FromAgent(
                        request.Agent)
                    : null,

            UniversalVerificationStrategies.BrowserReadback =>
                request.Browser is not null
                    ? adapters.FromBrowser(
                        request.Browser)
                    : null,

            UniversalVerificationStrategies.CodingVerificationGate =>
                request.Coding is not null
                    ? adapters.FromCoding(
                        request.Coding)
                    : null,

            UniversalVerificationStrategies.ComputerOperatorVerifier =>
                request.Desktop is not null
                    ? adapters.FromDesktop(
                        request.Desktop)
                    : request.Agent is not null
                        ? adapters.FromAgent(
                            request.Agent)
                        : null,

            UniversalVerificationStrategies.ConnectorReadback =>
                request.Agent is not null
                    ? adapters.FromAgent(
                        request.Agent)
                    : null,

            UniversalVerificationStrategies.SemanticVerifier =>
                null,

            _ =>
                null
        };
    }
}
