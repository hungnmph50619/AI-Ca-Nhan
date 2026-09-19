namespace PersonalAI.Web.Models;

public static class BrowserAgentCapabilities
{
    public const string SessionInfo = "session-info";
    public const string Navigate = "navigate";
    public const string PageObserve = "page-observe";
    public const string LinkOpen = "link-open";
}

public sealed record BrowserAgentStatusResponse(
    string Version,
    string Engine,
    bool Supported,
    bool JavaScriptEnabled,
    bool CookiesEnabled,
    bool DownloadsEnabled,
    bool FormSubmissionEnabled,
    int MaximumResponseBytes,
    int MaximumPageTextCharacters,
    int MaximumLinks,
    int MaximumRedirects,
    IReadOnlyList<string> AvailableCapabilities,
    IReadOnlyList<string> Limitations);

public sealed record BrowserSessionInfo(
    string WorkspaceId,
    bool HasPage,
    string? Url,
    string? Origin,
    string? Title,
    int? StatusCode,
    string? ContentType,
    DateTimeOffset? RetrievedAt,
    int TextCharacters,
    int LinkCount,
    int NavigationCount);

public sealed record BrowserPageLink(
    int Index,
    string Text,
    string Url,
    bool SameOrigin);

public sealed record BrowserPageObservation(
    string WorkspaceId,
    string Url,
    string Origin,
    string Title,
    int StatusCode,
    string ContentType,
    DateTimeOffset RetrievedAt,
    string Text,
    bool TextTruncated,
    IReadOnlyList<BrowserPageLink> Links,
    int NavigationCount);

public sealed record BrowserNavigationResult(
    string WorkspaceId,
    string RequestedUrl,
    string FinalUrl,
    string Origin,
    string Title,
    int StatusCode,
    string ContentType,
    int ResponseBytes,
    int RedirectCount,
    int TextCharacters,
    int LinkCount,
    DateTimeOffset RetrievedAt,
    int NavigationCount);
