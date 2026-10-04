using System.Text.RegularExpressions;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record BrowserExecutionBackendResult(
    bool Success,
    string Summary,
    IReadOnlyList<string> Evidence,
    bool ChangedExternalState,
    bool Verified,
    string Engine);

public interface IBrowserExecutionBackend
{
    string Engine { get; }

    bool CanHandle(
        ExecutionAgentRequest request,
        out double confidence,
        out string reason);

    Task<BrowserExecutionBackendResult> ExecuteAsync(
        ExecutionAgentRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class InternalHttpBrowserExecutionBackend(
    IBrowserAgentService browser)
    : IBrowserExecutionBackend
{
    private static readonly Regex UrlRegex = new(
        @"https?://[^s]+",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.Compiled);

    public string Engine => "internal-http-html";

    public bool CanHandle(
        ExecutionAgentRequest request,
        out double confidence,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Channel.Equals(
                ExecutionAgentChannels.Browser,
                StringComparison.OrdinalIgnoreCase))
        {
            confidence = 0;
            reason = "Backend chỉ nhận channel browser.";
            return false;
        }

        var match = UrlRegex.Match(
            request.Goal ?? string.Empty);

        if (!match.Success)
        {
            confidence = 0.35;
            reason =
                "Browser backend hiện tại chỉ tự thực thi khi goal chứa URL http/https rõ ràng.";
            return false;
        }

        confidence = 0.97;
        reason =
            "Goal chứa URL rõ ràng và phù hợp Browser Agent HTTP/HTML.";
        return true;
    }

    public async Task<BrowserExecutionBackendResult> ExecuteAsync(
        ExecutionAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!CanHandle(
                request,
                out _,
                out var reason))
        {
            throw new AgentValidationException(reason);
        }

        var match = UrlRegex.Match(
            request.Goal ?? string.Empty);
        var url = match.Value.TrimEnd(
            '.', ',', ';', ')', ']', '}');

        var navigation = await browser.NavigateAsync(
            url,
            cancellationToken);

        var observation = browser.ObservePage(
            maximumCharacters: 8_000,
            maximumLinks: 30);

        var evidence = new[]
        {
            $"URL cuối: {navigation.FinalUrl}",
            $"HTTP {navigation.StatusCode}; content={navigation.ContentType}",
            $"Tiêu đề: {observation.Title}",
            $"Text={observation.Text.Length} ký tự; links={observation.Links.Count}"
        };

        return new(
            Success: navigation.StatusCode is >= 200 and < 400,
            Summary:
                $"Đã điều hướng và quan sát trang {navigation.FinalUrl} bằng Browser Agent nội bộ.",
            evidence,
            ChangedExternalState: true,
            Verified:
                navigation.StatusCode is >= 200 and < 400 &&
                observation.Url.Equals(
                    navigation.FinalUrl,
                    StringComparison.OrdinalIgnoreCase),
            Engine);
    }
}

public sealed class BrowserExecutionAgent(
    IBrowserExecutionBackend backend)
    : IExecutionAgent
{
    public const string AgentId =
        "execution.browser-agent";

    public ExecutionAgentDefinition Definition { get; } = new(
        AgentId,
        "Browser Agent",
        "Execution agent cho tác vụ web có cấu trúc. Engine browser nằm sau adapter và có thể thay thế mà không đổi contract của router.",
        [
            "browser-navigation",
            "page-observation",
            "structured-web-access",
            "verification"
        ],
        [ExecutionAgentChannels.Browser],
        HasSideEffects: true,
        RequiresExplicitInvocation: true,
        SupportsVerification: true,
        SupportsRecovery: false);

    public bool CanHandle(
        ExecutionAgentRequest request,
        out double confidence,
        out string reason) =>
        backend.CanHandle(
            request,
            out confidence,
            out reason);

    public async Task<ExecutionAgentResult> ExecuteAsync(
        ExecutionAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await backend.ExecuteAsync(
            request,
            cancellationToken);

        return new(
            AgentId,
            result.Success
                ? AgentExecutionStatuses.Succeeded
                : AgentExecutionStatuses.Failed,
            result.Summary,
            result.Evidence,
            result.ChangedExternalState,
            result.Verified,
            Provider: "browser",
            Model: result.Engine);
    }
}
