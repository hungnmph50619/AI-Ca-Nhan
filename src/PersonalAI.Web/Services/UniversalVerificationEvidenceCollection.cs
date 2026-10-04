using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record UniversalVerificationEvidenceCollectionRequest(
    UniversalTaskRoutePreview Route,
    ToolExecutionResponse Execution,
    CodingVerificationRequest? CodingVerification = null,
    DesktopVerificationRoutingResult? DesktopVerification = null);

public sealed record UniversalVerificationEvidenceCollectionResult(
    string Strategy,
    bool Collected,
    UniversalOutcomeEvidence? Evidence,
    string Reason);

public interface IUniversalVerificationEvidenceCollectionService
{
    Task<UniversalVerificationEvidenceCollectionResult> CollectAsync(
        UniversalVerificationEvidenceCollectionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class UniversalVerificationEvidenceCollectionService(
    IUniversalVerificationEvidenceRouter router,
    IUniversalVerificationEvidenceAdapters adapters,
    IBrowserAgentService browser,
    ICodingVerificationGate codingVerification)
    : IUniversalVerificationEvidenceCollectionService
{
    public async Task<UniversalVerificationEvidenceCollectionResult> CollectAsync(
        UniversalVerificationEvidenceCollectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Route);
        ArgumentNullException.ThrowIfNull(request.Execution);

        var route = router.Route(
            new UniversalVerificationEvidenceRouteRequest(
                request.Route,
                request.Execution));

        switch (route.Strategy)
        {
            case UniversalVerificationStrategies.None:
                return new(
                    route.Strategy,
                    Collected: false,
                    Evidence: null,
                    route.Reason);

            case UniversalVerificationStrategies.BrowserReadback:
                return CollectBrowserReadback(
                    route);

            case UniversalVerificationStrategies.CodingVerificationGate:
                return await CollectCodingAsync(
                    route,
                    request.CodingVerification,
                    cancellationToken);

            case UniversalVerificationStrategies.ComputerOperatorVerifier:
                return CollectDesktop(
                    route,
                    request.DesktopVerification);

            case UniversalVerificationStrategies.LocalToolVerifier:
                return new(
                    route.Strategy,
                    Collected: false,
                    Evidence: null,
                    "Tool có verifier riêng nhưng chưa có generic tool-verifier invocation contract ở v4.0.9.");

            case UniversalVerificationStrategies.ConnectorReadback:
                return new(
                    route.Strategy,
                    Collected: false,
                    Evidence: null,
                    "Connector readback cần adapter nguồn đích; chưa tự giả lập evidence.");

            case UniversalVerificationStrategies.SemanticVerifier:
                return new(
                    route.Strategy,
                    Collected: false,
                    Evidence: null,
                    "Semantic verifier phải được gọi ở lớp AI riêng; collection service không tự bịa evidence.");

            default:
                return new(
                    route.Strategy,
                    Collected: false,
                    Evidence: null,
                    $"Verification strategy chưa được collection service hỗ trợ: {route.Strategy}.");
        }
    }

    private UniversalVerificationEvidenceCollectionResult CollectBrowserReadback(
        UniversalVerificationEvidenceRoute route)
    {
        try
        {
            var observation = browser.ObservePage(
                maximumCharacters: 8_000,
                maximumLinks: 30);

            var passed =
                observation.StatusCode is >= 200 and < 400 &&
                !string.IsNullOrWhiteSpace(observation.Url) &&
                !string.IsNullOrWhiteSpace(observation.Title);

            var confidence = passed
                ? 0.94
                : 0.91;

            var evidence = new UniversalOutcomeEvidence(
                "browser-structured-readback",
                passed,
                confidence,
                $"url={observation.Url}; status={observation.StatusCode}; title={observation.Title}; textChars={observation.Text.Length}; links={observation.Links.Count}");

            return new(
                route.Strategy,
                Collected: true,
                evidence,
                passed
                    ? "Đã đọc lại browser state sau execution."
                    : "Đã đọc lại browser state nhưng state hiện tại chưa đạt tiêu chí cấu trúc.");
        }
        catch (ToolExecutionInputException exception)
        {
            return new(
                route.Strategy,
                Collected: false,
                Evidence: null,
                $"Không thu được browser readback: {exception.Message}");
        }
    }

    private async Task<UniversalVerificationEvidenceCollectionResult> CollectCodingAsync(
        UniversalVerificationEvidenceRoute route,
        CodingVerificationRequest? verificationRequest,
        CancellationToken cancellationToken)
    {
        if (verificationRequest is null)
        {
            return new(
                route.Strategy,
                Collected: false,
                Evidence: null,
                "Coding verification cần TargetPath + RepositoryPath; thiếu context nên không tự suy đoán.");
        }

        var report = await codingVerification.VerifyAsync(
            verificationRequest,
            cancellationToken);

        var evidence = adapters.FromCoding(
            report);

        return new(
            route.Strategy,
            Collected: true,
            evidence,
            report.Summary);
    }

    private UniversalVerificationEvidenceCollectionResult CollectDesktop(
        UniversalVerificationEvidenceRoute route,
        DesktopVerificationRoutingResult? desktopVerification)
    {
        if (desktopVerification is null)
        {
            return new(
                route.Strategy,
                Collected: false,
                Evidence: null,
                "Desktop verification cần local observation/verification result của action vừa chạy; thiếu context nên không bịa evidence.");
        }

        var evidence = adapters.FromDesktop(
            desktopVerification);

        if (evidence is null)
        {
            return new(
                route.Strategy,
                Collected: false,
                Evidence: null,
                "Local desktop verifier yêu cầu semantic Vision; collection service chuyển tiếp thay vì tự kết luận.");
        }

        return new(
            route.Strategy,
            Collected: true,
            evidence,
            desktopVerification.Reason);
    }
}
