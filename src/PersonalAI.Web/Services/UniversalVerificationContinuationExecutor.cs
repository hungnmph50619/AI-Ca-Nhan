using System.Text.RegularExpressions;

namespace PersonalAI.Web.Services;

public static class UniversalVerificationContinuationStatuses
{
    public const string Verified = "verified";
    public const string Pending = "pending";
    public const string Unsupported = "unsupported";
}

public sealed record UniversalVerificationContinuationExecution(
    string Status,
    UniversalOutcomeEvidence? Evidence,
    string Reason);

public interface IUniversalVerificationContinuationExecutor
{
    Task<UniversalVerificationContinuationExecution> ExecuteAsync(
        UniversalVerificationContinuationPlan plan,
        UniversalTaskRoutePreview route,
        ExecutionAgentResult execution,
        CancellationToken cancellationToken = default);
}

public sealed class UniversalVerificationContinuationExecutor(
    IBrowserAgentService browser)
    : IUniversalVerificationContinuationExecutor
{
    private static readonly Regex UrlRegex = new(
        @"https?://[^\s]+",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.Compiled);

    public Task<UniversalVerificationContinuationExecution> ExecuteAsync(
        UniversalVerificationContinuationPlan plan,
        UniversalTaskRoutePreview route,
        ExecutionAgentResult execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(execution);

        cancellationToken.ThrowIfCancellationRequested();

        if (!plan.Required)
        {
            return Task.FromResult(
                new UniversalVerificationContinuationExecution(
                    UniversalVerificationContinuationStatuses.Unsupported,
                    Evidence: null,
                    "Continuation verification không được yêu cầu."));
        }

        return plan.Strategy switch
        {
            UniversalVerificationStrategies.BrowserReadback =>
                Task.FromResult(
                    ExecuteBrowserReadback(
                        route)),

            UniversalVerificationStrategies.CodingVerificationGate =>
                Task.FromResult(
                    Pending(
                        "Coding continuation cần TargetPath và RepositoryPath; không được chạy lại coding agent để đoán context.")),

            UniversalVerificationStrategies.ComputerOperatorVerifier =>
                Task.FromResult(
                    Pending(
                        "Desktop continuation cần decision + observation + frame difference mới; không được replay action cũ.")),

            UniversalVerificationStrategies.ConnectorReadback =>
                Task.FromResult(
                    Pending(
                        "Connector continuation cần provider-specific readback context; chưa có handler an toàn chung.")),

            _ =>
                Task.FromResult(
                    Pending(
                        $"Chưa có safe continuation handler cho strategy={plan.Strategy}."))
        };
    }

    private UniversalVerificationContinuationExecution ExecuteBrowserReadback(
        UniversalTaskRoutePreview route)
    {
        var match = UrlRegex.Match(
            route.Goal ?? string.Empty);

        if (!match.Success)
        {
            return Pending(
                "Browser readback cần URL http/https rõ ràng trong goal để xác minh mà không suy đoán.");
        }

        var expectedUrl = match.Value.TrimEnd(
            '.', ',', ';', ')', ']', '}');

        var observation = browser.ObservePage(
            maximumCharacters: 8_000,
            maximumLinks: 30);

        var observedUrl =
            (observation.Url ?? string.Empty).Trim();

        var urlMatches =
            observedUrl.Equals(
                expectedUrl,
                StringComparison.OrdinalIgnoreCase);

        var hasReadableState =
            !string.IsNullOrWhiteSpace(
                observation.Title) ||
            !string.IsNullOrWhiteSpace(
                observation.Text);

        if (!urlMatches)
        {
            return new(
                UniversalVerificationContinuationStatuses.Verified,
                new UniversalOutcomeEvidence(
                    "browser-structured-readback",
                    Passed: false,
                    Confidence: 0.98,
                    $"URL hiện tại '{observedUrl}' không khớp URL đích '{expectedUrl}'."),
                "Structured readback xác nhận browser chưa ở đúng URL đích.");
        }

        if (!hasReadableState)
        {
            return Pending(
                "Browser đang ở đúng URL nhưng structured readback chưa có title/text đủ để xác minh outcome.");
        }

        return new(
            UniversalVerificationContinuationStatuses.Verified,
            new UniversalOutcomeEvidence(
                "browser-structured-readback",
                Passed: true,
                Confidence: 0.97,
                $"Readback xác nhận URL={observedUrl}; title='{observation.Title}'; textChars={observation.Text.Length}."),
            "Structured browser readback đã tạo universal outcome evidence.");
    }

    private static UniversalVerificationContinuationExecution Pending(
        string reason) =>
        new(
            UniversalVerificationContinuationStatuses.Pending,
            Evidence: null,
            reason);
}
