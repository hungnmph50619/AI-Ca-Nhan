using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IBrowserAgentService
{
    BrowserAgentStatusResponse GetStatus();

    BrowserSessionInfo GetSessionInfo();

    Task<BrowserNavigationResult> NavigateAsync(
        string url,
        CancellationToken cancellationToken = default);

    Task<BrowserNavigationResult> OpenLinkAsync(
        int linkIndex,
        CancellationToken cancellationToken = default);

    BrowserPageObservation ObservePage(
        int maximumCharacters = BrowserAgentService.MaximumPageTextCharacters,
        int maximumLinks = BrowserAgentService.MaximumLinks);
}

public sealed class BrowserAgentService : IBrowserAgentService, IDisposable
{
    public const int MaximumResponseBytes = 1 * 1024 * 1024;
    public const int MaximumPageTextCharacters = 24_000;
    public const int MaximumLinks = 100;
    public const int MaximumRedirects = 5;
    public const int MaximumNavigationHistory = 20;
    public const int MaximumUrlCharacters = 2_048;
    public const int RequestTimeoutSeconds = 20;

    private static readonly Regex TitleRegex = new(
        @"<title\b[^>]*>(?<value>.*?)</title>",
        RegexOptions.IgnoreCase
        | RegexOptions.Singleline
        | RegexOptions.Compiled);

    private static readonly Regex ScriptLikeRegex = new(
        @"<(script|style|noscript|svg)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase
        | RegexOptions.Singleline
        | RegexOptions.Compiled);

    private static readonly Regex BreakRegex = new(
        @"<(br\s*/?|/p|/div|/li|/tr|/h[1-6]|/section|/article|/header|/footer|/main|/nav)\s*>",
        RegexOptions.IgnoreCase
        | RegexOptions.Compiled);

    private static readonly Regex TagRegex = new(
        @"<[^>]+>",
        RegexOptions.Singleline
        | RegexOptions.Compiled);

    private static readonly Regex SpaceRegex = new(
        @"[\t\f\v ]+",
        RegexOptions.Compiled);

    private static readonly Regex NewlineRegex = new(
        @"\r?\n\s*\r?\n+",
        RegexOptions.Compiled);

    private static readonly Regex LinkRegex = new(
        @"<a\b[^>]*?href\s*=\s*(?:(?:""(?<dq>[^""]*)"")|(?:'(?<sq>[^']*)')|(?<bare>[^\s>]+))[^>]*>(?<text>.*?)</a\s*>",
        RegexOptions.IgnoreCase
        | RegexOptions.Singleline
        | RegexOptions.Compiled);

    private readonly IWorkspaceContextAccessor _workspaceContext;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, BrowserSessionState> _sessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionGates =
        new(StringComparer.OrdinalIgnoreCase);

    public BrowserAgentService(
        IWorkspaceContextAccessor workspaceContext)
    {
        _workspaceContext = workspaceContext;

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression =
                DecompressionMethods.GZip
                | DecompressionMethods.Deflate
                | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 4
        };
        handler.ConnectCallback = ConnectPublicAsync;

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "PersonalAI-BrowserAgent",
                "1.2"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/plain", 0.9));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/xhtml+xml", 0.9));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json", 0.7));
    }

    public BrowserAgentStatusResponse GetStatus() =>
        new(
            PersonalAiRelease.Version,
            "http-html",
            Supported: true,
            JavaScriptEnabled: false,
            CookiesEnabled: false,
            DownloadsEnabled: false,
            FormSubmissionEnabled: false,
            MaximumResponseBytes,
            MaximumPageTextCharacters,
            MaximumLinks,
            MaximumRedirects,
            [
                BrowserAgentCapabilities.SessionInfo,
                BrowserAgentCapabilities.Navigate,
                BrowserAgentCapabilities.PageObserve,
                BrowserAgentCapabilities.LinkOpen
            ],
            [
                "Chỉ hỗ trợ HTTP GET qua cổng chuẩn 80/443.",
                "Không chạy JavaScript và không có Chromium/DOM renderer trong v1.2.0.",
                "Không lưu cookie, localStorage hoặc phiên đăng nhập.",
                "Không submit form, POST/PUT/PATCH/DELETE, download hoặc upload.",
                "Chặn loopback, private network, link-local, multicast và địa chỉ reserved để giảm rủi ro SSRF.",
                "Mỗi redirect được kiểm tra lại URL và DNS trước khi kết nối."
            ]);

    public BrowserSessionInfo GetSessionInfo()
    {
        var workspaceId = _workspaceContext.CurrentWorkspaceId;
        if (!_sessions.TryGetValue(
            workspaceId,
            out var session)
            || session.Page is null)
        {
            return new BrowserSessionInfo(
                workspaceId,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                0,
                0,
                session?.NavigationCount ?? 0);
        }

        var page = session.Page;
        return new BrowserSessionInfo(
            workspaceId,
            true,
            page.Url.ToString(),
            OriginOf(page.Url),
            page.Title,
            page.StatusCode,
            page.ContentType,
            page.RetrievedAt,
            page.Text.Length,
            page.Links.Count,
            session.NavigationCount);
    }

    public Task<BrowserNavigationResult> NavigateAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        var target = ParseAndValidateUri(url);
        return NavigateCoreAsync(
            target,
            url.Trim(),
            cancellationToken);
    }

    public Task<BrowserNavigationResult> OpenLinkAsync(
        int linkIndex,
        CancellationToken cancellationToken = default)
    {
        var workspaceId = _workspaceContext.CurrentWorkspaceId;
        if (!_sessions.TryGetValue(
            workspaceId,
            out var session)
            || session.Page is null)
        {
            throw new ToolExecutionInputException(
                "Browser session chưa có trang hiện tại để mở liên kết.");
        }

        var link = session.Page.Links.FirstOrDefault(
            item => item.Index == linkIndex);
        if (link is null)
        {
            throw new ToolExecutionInputException(
                "Link index không tồn tại trong snapshot trang hiện tại.");
        }

        var target = ParseAndValidateUri(link.Url);
        return NavigateCoreAsync(
            target,
            link.Url,
            cancellationToken);
    }

    public BrowserPageObservation ObservePage(
        int maximumCharacters = MaximumPageTextCharacters,
        int maximumLinks = MaximumLinks)
    {
        var workspaceId = _workspaceContext.CurrentWorkspaceId;
        if (!_sessions.TryGetValue(
            workspaceId,
            out var session)
            || session.Page is null)
        {
            throw new ToolExecutionInputException(
                "Browser session chưa có trang hiện tại. Hãy điều hướng tới một URL trước.");
        }

        var safeCharacters = Math.Clamp(
            maximumCharacters,
            1,
            MaximumPageTextCharacters);
        var safeLinks = Math.Clamp(
            maximumLinks,
            0,
            MaximumLinks);
        var page = session.Page;
        var text = page.Text.Length <= safeCharacters
            ? page.Text
            : page.Text[..safeCharacters];

        return new BrowserPageObservation(
            workspaceId,
            page.Url.ToString(),
            OriginOf(page.Url),
            page.Title,
            page.StatusCode,
            page.ContentType,
            page.RetrievedAt,
            text,
            page.Text.Length > safeCharacters,
            page.Links.Take(safeLinks).ToArray(),
            session.NavigationCount);
    }

    private async Task<BrowserNavigationResult> NavigateCoreAsync(
        Uri initialUri,
        string requestedUrl,
        CancellationToken cancellationToken)
    {
        var workspaceId = _workspaceContext.CurrentWorkspaceId;
        var gate = _sessionGates.GetOrAdd(
            workspaceId,
            _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = initialUri;
            var redirectCount = 0;

            while (true)
            {
                await ValidatePublicHostAsync(
                    current,
                    cancellationToken);

                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    current);
                request.Headers.Referrer = null;

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount >= MaximumRedirects)
                    {
                        throw new ToolExecutionInputException(
                            $"Trang chuyển hướng quá {MaximumRedirects} lần.");
                    }

                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        throw new ToolExecutionInputException(
                            "Server trả redirect nhưng không có Location hợp lệ.");
                    }

                    var next = location.IsAbsoluteUri
                        ? location
                        : new Uri(current, location);
                    current = ParseAndValidateUri(next.ToString());
                    redirectCount++;
                    continue;
                }

                var contentType = NormalizeContentType(
                    response.Content.Headers.ContentType?.MediaType);
                if (!IsAllowedContentType(contentType))
                {
                    throw new ToolExecutionInputException(
                        $"Browser Agent chỉ đọc nội dung text/HTML/JSON. Content-Type nhận được: {contentType ?? "không xác định"}.");
                }

                if (response.Content.Headers.ContentDisposition?.DispositionType
                    ?.Equals(
                        "attachment",
                        StringComparison.OrdinalIgnoreCase) == true)
                {
                    throw new ToolExecutionInputException(
                        "Browser Agent v1.2 không tải tệp đính kèm.");
                }

                var declaredLength = response.Content.Headers.ContentLength;
                if (declaredLength.HasValue
                    && declaredLength.Value > MaximumResponseBytes)
                {
                    throw new ToolExecutionInputException(
                        $"Response vượt giới hạn {MaximumResponseBytes / 1024} KB.");
                }

                var bytes = await ReadLimitedAsync(
                    response.Content,
                    MaximumResponseBytes,
                    cancellationToken);
                var textPayload = DecodeText(
                    bytes,
                    response.Content.Headers.ContentType?.CharSet);

                var finalUri = response.RequestMessage?.RequestUri
                    ?? current;
                finalUri = ParseAndValidateUri(finalUri.ToString());

                var parsed = ParsePage(
                    finalUri,
                    checked((int)response.StatusCode),
                    contentType ?? "text/plain",
                    textPayload);

                var state = _sessions.GetOrAdd(
                    workspaceId,
                    _ => new BrowserSessionState());

                state.NavigationCount = checked(
                    state.NavigationCount + 1);
                state.Page = parsed;
                state.History.Add(finalUri.ToString());
                if (state.History.Count > MaximumNavigationHistory)
                {
                    state.History.RemoveRange(
                        0,
                        state.History.Count - MaximumNavigationHistory);
                }

                return new BrowserNavigationResult(
                    workspaceId,
                    requestedUrl,
                    finalUri.ToString(),
                    OriginOf(finalUri),
                    parsed.Title,
                    parsed.StatusCode,
                    parsed.ContentType,
                    bytes.Length,
                    redirectCount,
                    parsed.Text.Length,
                    parsed.Links.Count,
                    parsed.RetrievedAt,
                    state.NavigationCount);
            }
        }
        catch (HttpRequestException exception)
        {
            throw new ToolExecutionInputException(
                $"Không thể tải trang qua Browser Agent: {SafeNetworkMessage(exception.Message)}");
        }
        catch (TaskCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            throw new ToolExecutionInputException(
                $"Browser Agent vượt quá thời gian chờ {RequestTimeoutSeconds} giây.");
        }
        finally
        {
            gate.Release();
        }
    }

    private static BrowserPageSnapshot ParsePage(
        Uri uri,
        int statusCode,
        string contentType,
        string payload)
    {
        if (contentType.Equals(
            "text/html",
            StringComparison.OrdinalIgnoreCase)
            || contentType.Equals(
                "application/xhtml+xml",
                StringComparison.OrdinalIgnoreCase))
        {
            var titleMatch = TitleRegex.Match(payload);
            var title = titleMatch.Success
                ? NormalizeVisibleText(
                    WebUtility.HtmlDecode(
                        StripTags(titleMatch.Groups["value"].Value)))
                : uri.Host;

            var cleaned = ScriptLikeRegex.Replace(
                payload,
                " ");
            cleaned = BreakRegex.Replace(
                cleaned,
                "\n");
            cleaned = TagRegex.Replace(
                cleaned,
                " ");
            cleaned = WebUtility.HtmlDecode(cleaned);
            var text = NormalizeVisibleText(cleaned);

            var links = ExtractLinks(
                uri,
                payload);

            return new BrowserPageSnapshot(
                uri,
                string.IsNullOrWhiteSpace(title)
                    ? uri.Host
                    : LimitInline(title, 300),
                statusCode,
                contentType,
                DateTimeOffset.UtcNow,
                text,
                links);
        }

        return new BrowserPageSnapshot(
            uri,
            uri.Host,
            statusCode,
            contentType,
            DateTimeOffset.UtcNow,
            NormalizeVisibleText(payload),
            []);
    }

    private static IReadOnlyList<BrowserPageLink> ExtractLinks(
        Uri pageUri,
        string html)
    {
        var result = new List<BrowserPageLink>();
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (Match match in LinkRegex.Matches(html))
        {
            if (result.Count >= MaximumLinks)
            {
                break;
            }

            var rawHref = match.Groups["dq"].Success
                ? match.Groups["dq"].Value
                : match.Groups["sq"].Success
                    ? match.Groups["sq"].Value
                    : match.Groups["bare"].Value;

            rawHref = WebUtility.HtmlDecode(
                rawHref.Trim());
            if (rawHref.Length == 0
                || rawHref.StartsWith("#", StringComparison.Ordinal)
                || rawHref.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                || rawHref.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || rawHref.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || rawHref.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Uri.TryCreate(
                pageUri,
                rawHref,
                out var resolved)
                || resolved is null
                || resolved.Scheme is not ("http" or "https"))
            {
                continue;
            }

            var normalized = StripFragment(resolved).ToString();
            if (normalized.Length > MaximumUrlCharacters
                || !seen.Add(normalized))
            {
                continue;
            }

            var text = NormalizeVisibleText(
                WebUtility.HtmlDecode(
                    StripTags(match.Groups["text"].Value)));
            if (string.IsNullOrWhiteSpace(text))
            {
                text = resolved.Host;
            }

            result.Add(
                new BrowserPageLink(
                    result.Count + 1,
                    LimitInline(text, 180),
                    normalized,
                    SameOrigin(pageUri, resolved)));
        }

        return result;
    }

    private static string StripTags(
        string value) =>
        TagRegex.Replace(
            value ?? string.Empty,
            " ");

    private static string NormalizeVisibleText(
        string value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");
        normalized = SpaceRegex.Replace(
            normalized,
            " ");
        normalized = NewlineRegex.Replace(
            normalized,
            "\n\n");
        return string.Join(
                "\n",
                normalized
                    .Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0))
            .Trim();
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(
            cancellationToken);
        using var output = new MemoryStream(
            Math.Min(maximumBytes, 64 * 1024));

        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(
                buffer,
                cancellationToken);
            if (read <= 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new ToolExecutionInputException(
                    $"Response vượt giới hạn {maximumBytes / 1024} KB.");
            }

            await output.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
        }

        return output.ToArray();
    }

    private static string DecodeText(
        byte[] bytes,
        string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(
                    charset.Trim('"', '\''))
                    .GetString(bytes);
            }
            catch
            {
                // Fall back to UTF-8 below.
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static Uri ParseAndValidateUri(
        string value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (raw.Length is < 1 or > MaximumUrlCharacters
            || !Uri.TryCreate(
                raw,
                UriKind.Absolute,
                out var uri)
            || uri is null)
        {
            throw new ToolExecutionInputException(
                "URL không hợp lệ hoặc vượt giới hạn độ dài.");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new ToolExecutionInputException(
                "Browser Agent chỉ cho phép URL http/https.");
        }

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            throw new ToolExecutionInputException(
                "URL chứa user/password không được phép.");
        }

        if ((uri.Scheme == "http" && uri.Port != 80)
            || (uri.Scheme == "https" && uri.Port != 443))
        {
            throw new ToolExecutionInputException(
                "Browser Agent v1.2 chỉ cho phép cổng chuẩn 80/443.");
        }

        var host = uri.DnsSafeHost
            .Trim()
            .TrimEnd('.');
        if (host.Length == 0
            || host.Equals(
                "localhost",
                StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(
                ".localhost",
                StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(
                ".local",
                StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(
                ".internal",
                StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(
                ".home.arpa",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolExecutionInputException(
                "Hostname local/private không được phép trong Browser Agent.");
        }

        if (IPAddress.TryParse(
            host,
            out var literal)
            && !IsPublicAddress(literal))
        {
            throw new ToolExecutionInputException(
                "Browser Agent chặn địa chỉ IP private/reserved.");
        }

        return StripFragment(uri);
    }

    private static async Task ValidatePublicHostAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        var addresses = await ResolveAddressesAsync(
            uri.DnsSafeHost,
            cancellationToken);
        if (addresses.Length == 0)
        {
            throw new ToolExecutionInputException(
                "Không phân giải được hostname.");
        }

        if (addresses.Any(address => !IsPublicAddress(address)))
        {
            throw new ToolExecutionInputException(
                "Hostname phân giải tới địa chỉ private/reserved nên bị Browser Agent chặn.");
        }
    }

    private static async ValueTask<Stream> ConnectPublicAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await ResolveAddressesAsync(
            context.DnsEndPoint.Host,
            cancellationToken);
        var allowed = addresses
            .Where(IsPublicAddress)
            .ToArray();

        if (allowed.Length == 0
            || allowed.Length != addresses.Length)
        {
            throw new HttpRequestException(
                "Hostname phân giải tới địa chỉ private/reserved.");
        }

        Exception? lastError = null;
        foreach (var address in allowed)
        {
            Socket? socket = null;
            try
            {
                socket = new Socket(
                    address.AddressFamily,
                    SocketType.Stream,
                    ProtocolType.Tcp);
                await socket.ConnectAsync(
                    address,
                    context.DnsEndPoint.Port,
                    cancellationToken);
                return new NetworkStream(
                    socket,
                    ownsSocket: true);
            }
            catch (Exception exception) when (
                exception is SocketException
                    or OperationCanceledException)
            {
                socket?.Dispose();
                lastError = exception;
                if (exception is OperationCanceledException)
                {
                    throw;
                }
            }
        }

        throw new HttpRequestException(
            "Không thể kết nối tới hostname đã xác thực.",
            lastError);
    }

    private static async Task<IPAddress[]> ResolveAddressesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(
            host,
            out var literal))
        {
            return [literal];
        }

        try
        {
            return await Dns.GetHostAddressesAsync(
                host,
                cancellationToken);
        }
        catch (SocketException exception)
        {
            throw new ToolExecutionInputException(
                $"Không phân giải được hostname: {SafeNetworkMessage(exception.Message)}");
        }
    }

    private static bool IsPublicAddress(
        IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var a = bytes[0];
            var b = bytes[1];

            return !(
                a == 0
                || a == 10
                || a == 127
                || (a == 100 && b is >= 64 and <= 127)
                || (a == 169 && b == 254)
                || (a == 172 && b is >= 16 and <= 31)
                || (a == 192 && b == 0)
                || (a == 192 && b == 168)
                || (a == 192 && b == 0 && bytes[2] == 2)
                || (a == 198 && b is 18 or 19)
                || (a == 198 && b == 51 && bytes[2] == 100)
                || (a == 203 && b == 0 && bytes[2] == 113)
                || a >= 224);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal
                || address.IsIPv6Multicast
                || address.IsIPv6SiteLocal)
            {
                return false;
            }

            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return false;
            }

            if (bytes[0] == 0x20
                && bytes[1] == 0x01
                && bytes[2] == 0x0D
                && bytes[3] == 0xB8)
            {
                return false;
            }

            return true;
        }

        return false;
    }

    private static bool IsAllowedContentType(
        string? contentType) =>
        contentType is "text/html"
            or "text/plain"
            or "application/xhtml+xml"
            or "application/json";

    private static string? NormalizeContentType(
        string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();

    private static bool IsRedirect(
        HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static Uri StripFragment(
        Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Fragment))
        {
            return uri;
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    private static bool SameOrigin(
        Uri left,
        Uri right) =>
        left.Scheme.Equals(
            right.Scheme,
            StringComparison.OrdinalIgnoreCase)
        && left.Host.Equals(
            right.Host,
            StringComparison.OrdinalIgnoreCase)
        && left.Port == right.Port;

    private static string OriginOf(
        Uri uri) =>
        uri.IsDefaultPort
            ? $"{uri.Scheme}://{uri.Host}"
            : $"{uri.Scheme}://{uri.Host}:{uri.Port}";

    private static string LimitInline(
        string value,
        int maximum)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= maximum
            ? normalized
            : normalized[..Math.Max(
                0,
                maximum - 1)] + "…";
    }

    private static string SafeNetworkMessage(
        string value)
    {
        var normalized = LimitInline(
            value,
            180);
        return string.IsNullOrWhiteSpace(normalized)
            ? "lỗi mạng"
            : normalized;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        foreach (var gate in _sessionGates.Values)
        {
            gate.Dispose();
        }
    }

    private sealed class BrowserSessionState
    {
        public BrowserPageSnapshot? Page { get; set; }

        public int NavigationCount { get; set; }

        public List<string> History { get; } = [];
    }

    private sealed record BrowserPageSnapshot(
        Uri Url,
        string Title,
        int StatusCode,
        string ContentType,
        DateTimeOffset RetrievedAt,
        string Text,
        IReadOnlyList<BrowserPageLink> Links);
}
