using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IConnectorService
{
    ConnectorStatusResponse GetStatus();

    ConnectorListResponse GetConnections();

    Task<ConnectorConnection> CreateAsync(
        CreateConnectorRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid connectorId,
        bool confirmed,
        CancellationToken cancellationToken = default);

    Task<ConnectorReadResult> ReadAsync(
        Guid connectorId,
        string path,
        CancellationToken cancellationToken = default);
}

public sealed class ConnectorConfirmationRequiredException(string message) : Exception(message);

public sealed class ConnectorService : IConnectorService, IDisposable
{
    public const int MaximumConnectionsPerWorkspace = 20;
    public const int MaximumResponseBytes = 512 * 1024;
    public const int MaximumReturnedCharacters = 32_000;
    public const int MaximumRedirects = 3;
    public const int MaximumPathCharacters = 1_024;
    public const int MaximumBearerTokenCharacters = 4_096;
    public const int RequestTimeoutSeconds = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _settingsPath;
    private readonly IDataProtector _protector;
    private readonly IWorkspaceContextAccessor _workspaceContext;
    private readonly IAuditRecorder _audit;
    private readonly ILogger<ConnectorService> _logger;
    private readonly HttpClient _httpClient;
    private StoredConnectorState _state;

    public ConnectorService(
        IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider,
        IWorkspaceContextAccessor workspaceContext,
        IAuditRecorder audit,
        ILogger<ConnectorService> logger)
    {
        _workspaceContext = workspaceContext;
        _audit = audit;
        _logger = logger;
        _protector = dataProtectionProvider.CreateProtector(
            "PersonalAI.Connectors.v1");

        var configuredRoot = configuration["Connectors:Root"]?.Trim();
        string directory;
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            directory = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(configuredRoot));
        }
        else
        {
            var localData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localData))
            {
                localData = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile),
                    ".personalai");
            }

            directory = Path.Combine(
                localData,
                "PersonalAI",
                "Connectors");
        }

        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(
            directory,
            "connections.json");
        _state = Load();

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
                "PersonalAI-Connector",
                "1.3"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "text/plain",
                0.8));
    }

    public ConnectorStatusResponse GetStatus() =>
        new(
            PersonalAiRelease.Version,
            Supported: true,
            AvailableKinds: [ConnectorKinds.HttpBearer],
            AvailableCapabilities:
            [
                ConnectorCapabilities.List,
                ConnectorCapabilities.Configure,
                ConnectorCapabilities.Read
            ],
            MaximumConnectionsPerWorkspace,
            MaximumResponseBytes,
            MaximumReturnedCharacters,
            MaximumRedirects,
            SecretsEncrypted: true,
            WriteActionsEnabled: false,
            Limitations:
            [
                "v1.3.0 chỉ hỗ trợ HTTPS GET với Bearer token.",
                "Không POST/PUT/PATCH/DELETE, upload, webhook send hoặc browser-cookie authentication.",
                "Base URL phải là public HTTPS origin trên cổng 443.",
                "Redirect chỉ được theo nếu giữ nguyên origin.",
                "Bearer token được mã hóa local bằng ASP.NET Core Data Protection và không được trả qua API.",
                "Connector response được coi là dữ liệu nhạy cảm."
            ]);

    public ConnectorListResponse GetConnections()
    {
        var workspaceId = _workspaceContext.CurrentWorkspaceId;
        lock (_gate)
        {
            var items = _state.Connections
                .Where(item => item.WorkspaceId.Equals(
                    workspaceId,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToPublic)
                .ToArray();

            return new ConnectorListResponse(
                workspaceId,
                MaximumConnectionsPerWorkspace,
                items);
        }
    }

    public async Task<ConnectorConnection> CreateAsync(
        CreateConnectorRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.Confirmed)
        {
            throw new ConnectorConfirmationRequiredException(
                "Cần xác nhận rõ ràng trước khi lưu credential connector.");
        }

        var workspaceId = _workspaceContext.CurrentWorkspaceId;
        var name = NormalizeName(request.Name);
        var kind = NormalizeKind(request.Kind);
        var baseUri = NormalizeBaseUri(request.BaseUrl);
        var token = NormalizeBearerToken(request.BearerToken);
        var now = DateTimeOffset.UtcNow;

        StoredConnectorConnection stored;
        lock (_gate)
        {
            var currentCount = _state.Connections.Count(item =>
                item.WorkspaceId.Equals(
                    workspaceId,
                    StringComparison.OrdinalIgnoreCase));
            if (currentCount >= MaximumConnectionsPerWorkspace)
            {
                throw new ArgumentException(
                    $"Workspace đã đạt giới hạn {MaximumConnectionsPerWorkspace} connector.");
            }

            if (_state.Connections.Any(item =>
                item.WorkspaceId.Equals(
                    workspaceId,
                    StringComparison.OrdinalIgnoreCase)
                && item.Name.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    "Tên connector đã tồn tại trong workspace hiện tại.");
            }

            stored = new StoredConnectorConnection
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Name = name,
                Kind = kind,
                BaseUrl = baseUri.ToString(),
                Enabled = true,
                EncryptedBearerToken = _protector.Protect(token),
                CreatedAt = now,
                UpdatedAt = now
            };

            _state.Connections.Add(stored);
            SaveLocked();
        }

        _audit.Record(
            AuditAgents.User,
            "connector.create",
            $"connector:{stored.Id:D}",
            "user-confirmed-connector-configuration",
            AuditResults.Succeeded,
            workspaceId: workspaceId);

        await Task.CompletedTask;
        return ToPublic(stored);
    }

    public Task DeleteAsync(
        Guid connectorId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!confirmed)
        {
            throw new ConnectorConfirmationRequiredException(
                "Cần xác nhận rõ ràng trước khi xóa connector và credential đã lưu.");
        }

        var workspaceId = _workspaceContext.CurrentWorkspaceId;
        StoredConnectorConnection removed;

        lock (_gate)
        {
            var index = _state.Connections.FindIndex(item =>
                item.Id == connectorId
                && item.WorkspaceId.Equals(
                    workspaceId,
                    StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                throw new KeyNotFoundException(
                    "Không tìm thấy connector trong workspace hiện tại.");
            }

            removed = _state.Connections[index];
            _state.Connections.RemoveAt(index);
            SaveLocked();
        }

        _audit.Record(
            AuditAgents.User,
            "connector.delete",
            $"connector:{removed.Id:D}",
            "user-confirmed-connector-configuration",
            AuditResults.Succeeded,
            workspaceId: workspaceId);

        return Task.CompletedTask;
    }

    public async Task<ConnectorReadResult> ReadAsync(
        Guid connectorId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var workspaceId = _workspaceContext.CurrentWorkspaceId;
        StoredConnectorConnection stored;

        lock (_gate)
        {
            stored = _state.Connections.FirstOrDefault(item =>
                item.Id == connectorId
                && item.WorkspaceId.Equals(
                    workspaceId,
                    StringComparison.OrdinalIgnoreCase))
                ?? throw new ToolExecutionInputException(
                    "Không tìm thấy connector trong workspace hiện tại.");

            stored = stored.Clone();
        }

        if (!stored.Enabled)
        {
            throw new ToolExecutionInputException(
                "Connector hiện đang bị tắt.");
        }

        var token = UnprotectToken(stored);
        var baseUri = NormalizeBaseUri(stored.BaseUrl);
        var requestedPath = NormalizeRelativePath(path);
        var current = ResolveSameOrigin(
            baseUri,
            requestedPath);
        var redirectCount = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ValidatePublicHostAsync(
                current,
                cancellationToken);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                current);
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount >= MaximumRedirects)
                {
                    throw new ToolExecutionInputException(
                        $"Connector response chuyển hướng quá {MaximumRedirects} lần.");
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
                next = NormalizeRequestUri(next);

                if (!SameOrigin(
                    baseUri,
                    next))
                {
                    throw new ToolExecutionInputException(
                        "Connector không chuyển Bearer credential qua redirect khác origin.");
                }

                current = next;
                redirectCount++;
                continue;
            }

            var contentType = NormalizeContentType(
                response.Content.Headers.ContentType?.MediaType);
            if (!IsAllowedContentType(contentType))
            {
                throw new ToolExecutionInputException(
                    $"Connector v1.3 chỉ đọc JSON/text. Content-Type nhận được: {contentType ?? "không xác định"}.");
            }

            if (response.Content.Headers.ContentDisposition?.DispositionType
                ?.Equals(
                    "attachment",
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new ToolExecutionInputException(
                    "Connector v1.3 không tải tệp đính kèm.");
            }

            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength.HasValue
                && declaredLength.Value > MaximumResponseBytes)
            {
                throw new ToolExecutionInputException(
                    $"Connector response vượt giới hạn {MaximumResponseBytes / 1024} KB.");
            }

            var bytes = await ReadLimitedAsync(
                response.Content,
                MaximumResponseBytes,
                cancellationToken);
            var decoded = DecodeText(
                bytes,
                response.Content.Headers.ContentType?.CharSet);
            var sanitized = RedactCredential(
                decoded,
                token);
            var truncated = sanitized.Length > MaximumReturnedCharacters;
            var returned = truncated
                ? sanitized[..MaximumReturnedCharacters]
                : sanitized;

            return new ConnectorReadResult(
                stored.Id,
                stored.Name,
                requestedPath,
                current.ToString(),
                checked((int)response.StatusCode),
                contentType ?? "text/plain",
                bytes.Length,
                redirectCount,
                returned,
                truncated,
                DateTimeOffset.UtcNow);
        }
    }

    private StoredConnectorState Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new StoredConnectorState();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<StoredConnectorState>(
                json,
                JsonOptions)
                ?? new StoredConnectorState();
            loaded.Normalize();
            return loaded;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            _logger.LogWarning(
                exception,
                "Không thể đọc connector settings. Bắt đầu với danh sách trống.");
            return new StoredConnectorState();
        }
    }

    private void SaveLocked()
    {
        var json = JsonSerializer.Serialize(
            _state,
            JsonOptions);
        var temporaryPath = $"{_settingsPath}.tmp";
        File.WriteAllText(
            temporaryPath,
            json,
            Encoding.UTF8);
        File.Move(
            temporaryPath,
            _settingsPath,
            true);
    }

    private string UnprotectToken(
        StoredConnectorConnection stored)
    {
        if (string.IsNullOrWhiteSpace(
            stored.EncryptedBearerToken))
        {
            throw new ToolExecutionInputException(
                "Connector không có credential khả dụng.");
        }

        try
        {
            return _protector.Unprotect(
                stored.EncryptedBearerToken);
        }
        catch (CryptographicException exception)
        {
            _logger.LogWarning(
                exception,
                "Không thể giải mã connector credential {ConnectorId}.",
                stored.Id);
            throw new ToolExecutionInputException(
                "Credential connector không thể giải mã. Hãy xóa và cấu hình lại connector.");
        }
    }

    private static ConnectorConnection ToPublic(
        StoredConnectorConnection item) =>
        new(
            item.Id,
            item.WorkspaceId,
            item.Name,
            item.Kind,
            item.BaseUrl,
            item.Enabled,
            !string.IsNullOrWhiteSpace(
                item.EncryptedBearerToken),
            item.CreatedAt,
            item.UpdatedAt);

    private static string NormalizeName(
        string value)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length is < 2 or > 80)
        {
            throw new ArgumentException(
                "Tên connector phải có từ 2 đến 80 ký tự.");
        }

        return normalized;
    }

    private static string NormalizeKind(
        string value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (!normalized.Equals(
            ConnectorKinds.HttpBearer,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Loại connector chưa được hỗ trợ.");
        }

        return normalized;
    }

    private static Uri NormalizeBaseUri(
        string value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (raw.Length is < 8 or > 2_048
            || !Uri.TryCreate(
                raw,
                UriKind.Absolute,
                out var uri)
            || uri is null)
        {
            throw new ArgumentException(
                "Base URL connector không hợp lệ.");
        }

        if (!uri.Scheme.Equals(
            "https",
            StringComparison.OrdinalIgnoreCase)
            || uri.Port != 443)
        {
            throw new ArgumentException(
                "Connector v1.3 chỉ cho phép public HTTPS origin trên cổng 443.");
        }

        if (!string.IsNullOrWhiteSpace(uri.UserInfo)
            || !string.IsNullOrWhiteSpace(uri.Query)
            || !string.IsNullOrWhiteSpace(uri.Fragment)
            || (uri.AbsolutePath != "/"
                && !string.IsNullOrWhiteSpace(
                    uri.AbsolutePath)))
        {
            throw new ArgumentException(
                "Base URL phải là origin dạng https://host/ và không chứa path, query, fragment hoặc user info.");
        }

        var host = uri.DnsSafeHost
            .Trim()
            .TrimEnd('.');
        if (host.Length == 0
            || IsBlockedHostName(host))
        {
            throw new ArgumentException(
                "Hostname local/private không được phép cho connector.");
        }

        if (IPAddress.TryParse(
            host,
            out var literal)
            && !IsPublicAddress(literal))
        {
            throw new ArgumentException(
                "Connector chặn địa chỉ IP private/reserved.");
        }

        return new Uri(
            $"https://{host}/",
            UriKind.Absolute);
    }

    private static string NormalizeBearerToken(
        string value)
    {
        var token = (value ?? string.Empty).Trim();
        if (token.Length is < 4 or > MaximumBearerTokenCharacters
            || token.Contains('\r')
            || token.Contains('\n'))
        {
            throw new ArgumentException(
                "Bearer token không hợp lệ.");
        }

        return token;
    }

    private static string NormalizeRelativePath(
        string value)
    {
        var path = (value ?? string.Empty).Trim();
        if (path.Length is < 1 or > MaximumPathCharacters
            || !path.StartsWith(
                "/",
                StringComparison.Ordinal)
            || path.StartsWith(
                "//",
                StringComparison.Ordinal)
            || path.Contains(
                '#',
                StringComparison.Ordinal)
            || path.Contains(
                '\\',
                StringComparison.Ordinal))
        {
            throw new ToolExecutionInputException(
                "Connector path phải bắt đầu bằng một dấu /, không chứa fragment/backslash và không vượt giới hạn.");
        }

        return path;
    }

    private static Uri ResolveSameOrigin(
        Uri baseUri,
        string path)
    {
        var resolved = new Uri(
            baseUri,
            path);
        resolved = NormalizeRequestUri(resolved);

        if (!SameOrigin(
            baseUri,
            resolved))
        {
            throw new ToolExecutionInputException(
                "Connector path không được đổi origin.");
        }

        return resolved;
    }

    private static Uri NormalizeRequestUri(
        Uri uri)
    {
        if (!uri.Scheme.Equals(
            "https",
            StringComparison.OrdinalIgnoreCase)
            || uri.Port != 443
            || !string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            throw new ToolExecutionInputException(
                "Connector chỉ cho phép HTTPS cùng origin trên cổng 443.");
        }

        var host = uri.DnsSafeHost
            .Trim()
            .TrimEnd('.');
        if (host.Length == 0
            || IsBlockedHostName(host))
        {
            throw new ToolExecutionInputException(
                "Hostname local/private không được phép cho connector.");
        }

        if (IPAddress.TryParse(
            host,
            out var literal)
            && !IsPublicAddress(literal))
        {
            throw new ToolExecutionInputException(
                "Connector chặn địa chỉ IP private/reserved.");
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    private static bool IsBlockedHostName(
        string host) =>
        host.Equals(
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
            StringComparison.OrdinalIgnoreCase);

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
                "Không phân giải được hostname connector.");
        }

        if (addresses.Any(address =>
            !IsPublicAddress(address)))
        {
            throw new ToolExecutionInputException(
                "Hostname connector phân giải tới địa chỉ private/reserved nên bị chặn.");
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
                "Hostname connector phân giải tới địa chỉ private/reserved.");
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
            "Không thể kết nối tới connector origin đã xác thực.",
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
                $"Không phân giải được hostname connector: {SafeInline(exception.Message, 160)}");
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
        if (address.AddressFamily
            == AddressFamily.InterNetwork)
        {
            var a = bytes[0];
            var b = bytes[1];

            return !(
                a == 0
                || a == 10
                || a == 127
                || (a == 100
                    && b is >= 64 and <= 127)
                || (a == 169 && b == 254)
                || (a == 172
                    && b is >= 16 and <= 31)
                || (a == 192 && b == 0)
                || (a == 192 && b == 2)
                || (a == 192 && b == 168)
                || (a == 198
                    && b is 18 or 19)
                || (a == 198
                    && b == 51
                    && bytes[2] == 100)
                || (a == 203
                    && b == 0
                    && bytes[2] == 113)
                || a >= 224);
        }

        if (address.AddressFamily
            == AddressFamily.InterNetworkV6)
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

    private static async Task<byte[]> ReadLimitedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(
            cancellationToken);
        using var output = new MemoryStream(
            Math.Min(
                maximumBytes,
                64 * 1024));

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
                    $"Connector response vượt giới hạn {maximumBytes / 1024} KB.");
            }

            await output.WriteAsync(
                buffer.AsMemory(
                    0,
                    read),
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
                return Encoding
                    .GetEncoding(
                        charset.Trim(
                            '"',
                            '\''))
                    .GetString(bytes);
            }
            catch
            {
                // UTF-8 fallback below.
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string? NormalizeContentType(
        string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();

    private static bool IsAllowedContentType(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals(
                "application/json",
                StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(
                "+json",
                StringComparison.OrdinalIgnoreCase)
            || value.Equals(
                "text/plain",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRedirect(
        HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static string RedactCredential(
        string value,
        string credential)
    {
        if (string.IsNullOrEmpty(value)
            || string.IsNullOrEmpty(credential))
        {
            return value;
        }

        return value.Replace(
            credential,
            "[REDACTED_CREDENTIAL]",
            StringComparison.Ordinal);
    }

    private static string SafeInline(
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
            : normalized[..maximum];
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed class StoredConnectorState
    {
        public List<StoredConnectorConnection> Connections { get; set; } = [];

        public void Normalize()
        {
            Connections ??= [];
            Connections = Connections
                .Where(item =>
                    item.Id != Guid.Empty
                    && !string.IsNullOrWhiteSpace(
                        item.WorkspaceId)
                    && !string.IsNullOrWhiteSpace(
                        item.Name)
                    && !string.IsNullOrWhiteSpace(
                        item.BaseUrl))
                .Select(item =>
                {
                    item.WorkspaceId = item.WorkspaceId
                        .Trim()
                        .ToLowerInvariant();
                    item.Name = item.Name.Trim();
                    item.Kind = string.IsNullOrWhiteSpace(
                        item.Kind)
                        ? ConnectorKinds.HttpBearer
                        : item.Kind
                            .Trim()
                            .ToLowerInvariant();
                    return item;
                })
                .ToList();
        }
    }

    private sealed class StoredConnectorConnection
    {
        public Guid Id { get; set; }

        public string WorkspaceId { get; set; } = "personal";

        public string Name { get; set; } = string.Empty;

        public string Kind { get; set; } = ConnectorKinds.HttpBearer;

        public string BaseUrl { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        public string EncryptedBearerToken { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public StoredConnectorConnection Clone() =>
            new()
            {
                Id = Id,
                WorkspaceId = WorkspaceId,
                Name = Name,
                Kind = Kind,
                BaseUrl = BaseUrl,
                Enabled = Enabled,
                EncryptedBearerToken = EncryptedBearerToken,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt
            };
    }
}
