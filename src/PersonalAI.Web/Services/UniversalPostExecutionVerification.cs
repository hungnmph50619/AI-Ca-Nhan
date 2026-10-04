using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record UniversalPostExecutionVerificationRequest(
    string Goal,
    UniversalTaskRoutePreview Route,
    ToolExecutionResponse Execution,
    CodingVerificationRequest? CodingVerification = null,
    DesktopVerificationRoutingResult? DesktopVerification = null);

public sealed record UniversalPostExecutionVerificationResult(
    UniversalVerificationEvidenceRoute EvidenceRoute,
    UniversalVerificationEvidenceCollectionResult Collection,
    UniversalOutcomeVerificationResult Verification,
    UniversalFallbackDecision Decision);

public interface IUniversalPostExecutionVerificationService
{
    Task<UniversalPostExecutionVerificationResult> VerifyAsync(
        UniversalPostExecutionVerificationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class UniversalPostExecutionVerificationService(
    IUniversalVerificationEvidenceRouter evidenceRouter,
    IUniversalVerificationEvidenceCollectionService evidenceCollection,
    IUniversalOutcomeVerificationService outcomeVerification,
    IUniversalFallbackPolicy fallbackPolicy)
    : IUniversalPostExecutionVerificationService
{
    public async Task<UniversalPostExecutionVerificationResult> VerifyAsync(
        UniversalPostExecutionVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Route);
        ArgumentNullException.ThrowIfNull(request.Execution);

        if (string.IsNullOrWhiteSpace(request.Goal))
        {
            throw new AgentValidationException(
                "Post-execution verification cần goal.");
        }

        var route = evidenceRouter.Route(
            new UniversalVerificationEvidenceRouteRequest(
                request.Route,
                request.Execution));

        var collection = await evidenceCollection.CollectAsync(
            new UniversalVerificationEvidenceCollectionRequest(
                request.Route,
                request.Execution,
                request.CodingVerification,
                request.DesktopVerification),
            cancellationToken);

        var verification = outcomeVerification.VerifyTool(
            new UniversalToolOutcomeVerificationRequest(
                request.Goal.Trim(),
                request.Execution,
                collection.Evidence));

        var decision = fallbackPolicy.Evaluate(
            request.Route,
            request.Execution,
            verification);

        return new(
            route,
            collection,
            verification,
            decision);
    }
}
