using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PersonalAI.Web.Services;

public sealed record PlaywrightPageObservation(
    bool Success,
    string RequestedUrl,
    string FinalUrl,
    string Title,
    string Text,
    int StatusCode,
    bool DomReadable,
    string Detail);

public interface IPlaywrightBrowserAdapter
{
    bool PreferredRuntimeAvailable { get; }

    Task<PlaywrightPageObservation> NavigateAndObserveAsync(
        string url,
        CancellationToken cancellationToken = default);
}

public sealed class PlaywrightBrowserAdapter(
    IBrowserAgentService safeBrowser,
    IWorkspaceContextAccessor workspaceContext,
    ILogger<PlaywrightBrowserAdapter> logger)
    : IPlaywrightBrowserAdapter, IAsyncDisposable
{
    private const int NavigationTimeoutMs = 15_000;
    private const int MaximumBodyCharacters = 24_000;

    private readonly SemaphoreSlim startupGate = new(1, 1);
    private readonly ConcurrentDictionary<string, IBrowserContext> contexts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IPage> pages =
        new(StringComparer.OrdinalIgnoreCase);

    private IPlaywright? playwright;
    private IBrowser? browser;

    public bool PreferredRuntimeAvailable =>
        OperatingSystem.IsWindows();

    public async Task<PlaywrightPageObservation> NavigateAndObserveAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        // Dùng Browser Agent hiện có làm cổng an toàn SSRF/DNS trước khi
        // Playwright được phép điều hướng một browser có JavaScript.
        var preflight = await safeBrowser.NavigateAsync(
            url,
            cancellationToken);

        if (!OperatingSystem.IsWindows())
        {
            return Failed(
                url,
                preflight.FinalUrl,
                preflight.StatusCode,
                "Playwright Edge adapter hiện ưu tiên Windows; dùng Browser Agent HTTP dự phòng.");
        }

        try
        {
            var page = await GetPageAsync(
                cancellationToken);

            IResponse? response;
            try
            {
                response = await page.GotoAsync(
                    preflight.FinalUrl,
                    new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = NavigationTimeoutMs
                    });
            }
            catch (TimeoutException)
            {
                return Failed(
                    url,
                    page.Url,
                    0,
                    "Playwright vượt quá thời gian chờ DOMContentLoaded.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await page.WaitForLoadStateAsync(
                    LoadState.NetworkIdle,
                    new PageWaitForLoadStateOptions
                    {
                        Timeout = 2_000
                    });
            }
            catch (TimeoutException)
            {
                // NetworkIdle không phải điều kiện bắt buộc; SPA có thể giữ
                // kết nối nền. DOMContentLoaded + DOM readback vẫn có giá trị.
            }

            var title = await page.TitleAsync();

            string bodyText;
            try
            {
                bodyText = await page.Locator("body")
                    .InnerTextAsync(
                        new LocatorInnerTextOptions
                        {
                            Timeout = 3_000
                        });
            }
            catch (TimeoutException)
            {
                bodyText = string.Empty;
            }

            bodyText = NormalizeAndLimit(
                bodyText,
                MaximumBodyCharacters);

            var statusCode =
                response?.Status ??
                preflight.StatusCode;

            var finalUrl =
                string.IsNullOrWhiteSpace(page.Url)
                    ? preflight.FinalUrl
                    : page.Url;

            var readable =
                !string.IsNullOrWhiteSpace(title) ||
                !string.IsNullOrWhiteSpace(bodyText);

            return new(
                Success:
                    statusCode is >= 200 and < 400 &&
                    readable,
                RequestedUrl: url,
                FinalUrl: finalUrl,
                Title: title ?? string.Empty,
                Text: bodyText,
                StatusCode: statusCode,
                DomReadable: readable,
                Detail: readable
                    ? "Playwright đã đọc DOM sau khi trang thực thi JavaScript."
                    : "Playwright đã điều hướng nhưng DOM chưa có title/body text đủ để xác minh.");
        }
        catch (PlaywrightException exception)
        {
            logger.LogDebug(
                exception,
                "Playwright Edge adapter không khả dụng.");

            return Failed(
                url,
                preflight.FinalUrl,
                preflight.StatusCode,
                $"Playwright/Edge không khả dụng: {Limit(exception.Message, 220)}");
        }
    }

    private async Task<IPage> GetPageAsync(
        CancellationToken cancellationToken)
    {
        var workspaceId =
            workspaceContext.CurrentWorkspaceId;

        if (pages.TryGetValue(
                workspaceId,
                out var existingPage))
        {
            return existingPage;
        }

        await startupGate.WaitAsync(
            cancellationToken);
        try
        {
            if (playwright is null)
                playwright = await Playwright.CreateAsync();

            if (browser is null)
            {
                browser = await playwright.Chromium.LaunchAsync(
                    new BrowserTypeLaunchOptions
                    {
                        Headless = true,
                        Channel = "msedge",
                        Timeout = 8_000
                    });
            }

            if (!contexts.TryGetValue(
                    workspaceId,
                    out var context))
            {
                context = await browser.NewContextAsync(
                    new BrowserNewContextOptions
                    {
                        AcceptDownloads = false,
                        JavaScriptEnabled = true
                    });

                contexts[workspaceId] = context;
            }

            if (!pages.TryGetValue(
                    workspaceId,
                    out var page))
            {
                page = await context.NewPageAsync();
                pages[workspaceId] = page;
            }

            return page;
        }
        finally
        {
            startupGate.Release();
        }
    }

    private static PlaywrightPageObservation Failed(
        string requestedUrl,
        string finalUrl,
        int statusCode,
        string detail) =>
        new(
            false,
            requestedUrl,
            finalUrl,
            string.Empty,
            string.Empty,
            statusCode,
            DomReadable: false,
            detail);

    private static string NormalizeAndLimit(
        string? value,
        int maximum)
    {
        var normalized =
            (value ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Trim();

        return normalized.Length <= maximum
            ? normalized
            : normalized[..maximum];
    }

    private static string Limit(
        string? value,
        int maximum)
    {
        var normalized = value ?? string.Empty;
        return normalized.Length <= maximum
            ? normalized
            : normalized[..maximum];
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var page in pages.Values)
        {
            try
            {
                await page.CloseAsync();
            }
            catch
            {
            }
        }

        foreach (var context in contexts.Values)
        {
            try
            {
                await context.CloseAsync();
            }
            catch
            {
            }
        }

        if (browser is not null)
        {
            try
            {
                await browser.CloseAsync();
            }
            catch
            {
            }
        }

        playwright?.Dispose();
        startupGate.Dispose();
    }
}

public interface IPlaywrightBrowserExecutionBackend
    : IBrowserExecutionBackend
{
}

public interface IHttpBrowserExecutionBackend
    : IBrowserExecutionBackend
{
}

public sealed class PlaywrightBrowserExecutionBackend(
    IPlaywrightBrowserAdapter playwright)
    : IPlaywrightBrowserExecutionBackend
{
    private static readonly Regex UrlRegex = new(
        @"https?://[^\s]+",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.Compiled);

    public string Engine => "playwright-msedge-dom";

    public bool CanHandle(
        ExecutionAgentRequest request,
        out double confidence,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validChannel =
            request.Channel.Equals(
                ExecutionAgentChannels.Browser,
                StringComparison.OrdinalIgnoreCase);

        var hasUrl =
            UrlRegex.IsMatch(
                request.Goal ?? string.Empty);

        if (!validChannel || !hasUrl)
        {
            confidence = 0;
            reason = "Playwright cần browser channel và URL http/https rõ ràng.";
            return false;
        }

        confidence =
            playwright.PreferredRuntimeAvailable
                ? 0.995
                : 0.60;

        reason =
            playwright.PreferredRuntimeAvailable
                ? "Windows có thể dùng Playwright với Microsoft Edge để đọc DOM thật."
                : "Playwright runtime ưu tiên chưa sẵn sàng; có thể fallback HTTP.";

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
            throw new AgentValidationException(
                reason);

        var match =
            UrlRegex.Match(
                request.Goal ?? string.Empty);

        var url =
            match.Value.TrimEnd(
                '.', ',', ';', ')', ']', '}');

        var observation =
            await playwright.NavigateAndObserveAsync(
                url,
                cancellationToken);

        return new(
            Success: observation.Success,
            Summary: observation.Success
                ? $"Playwright đã điều hướng và đọc DOM tại {observation.FinalUrl}."
                : $"Playwright chưa xác minh được DOM: {observation.Detail}",
            Evidence:
            [
                $"URL cuối: {observation.FinalUrl}",
                $"HTTP {observation.StatusCode}",
                $"Tiêu đề DOM: {observation.Title}",
                $"DOM text={observation.Text.Length} ký tự",
                observation.Detail
            ],
            ChangedExternalState: observation.Success,
            Verified:
                observation.Success &&
                observation.DomReadable,
            Engine);
    }
}

public sealed class CompositeBrowserExecutionBackend(
    IPlaywrightBrowserExecutionBackend playwright,
    IHttpBrowserExecutionBackend http)
    : IBrowserExecutionBackend
{
    public string Engine => "browser-capability-router";

    public bool CanHandle(
        ExecutionAgentRequest request,
        out double confidence,
        out string reason)
    {
        var pw =
            playwright.CanHandle(
                request,
                out var pwConfidence,
                out var pwReason);

        var fallback =
            http.CanHandle(
                request,
                out var httpConfidence,
                out var httpReason);

        if (!pw && !fallback)
        {
            confidence = Math.Max(
                pwConfidence,
                httpConfidence);
            reason =
                $"Không backend browser nào đủ capability. Playwright: {pwReason} HTTP: {httpReason}";
            return false;
        }

        confidence = Math.Max(
            pwConfidence,
            httpConfidence);

        reason = pw && pwConfidence >= httpConfidence
            ? $"Ưu tiên Playwright/DOM. {pwReason}"
            : $"Ưu tiên HTTP structured fallback. {httpReason}";

        return true;
    }

    public async Task<BrowserExecutionBackendResult> ExecuteAsync(
        ExecutionAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (playwright.CanHandle(
                request,
                out var pwConfidence,
                out _) &&
            http.CanHandle(
                request,
                out var httpConfidence,
                out _) &&
            pwConfidence >= httpConfidence)
        {
            var structured =
                await playwright.ExecuteAsync(
                    request,
                    cancellationToken);

            if (structured.Success &&
                structured.Verified)
                return structured;
        }

        return await http.ExecuteAsync(
            request,
            cancellationToken);
    }
}
