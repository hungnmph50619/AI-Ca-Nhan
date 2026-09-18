namespace PersonalAI.Web.Models;

public static class ConnectorKinds
{
    public const string HttpBearer = "http-bearer";
}

public static class ConnectorCapabilities
{
    public const string List = "list";
    public const string Configure = "configure";
    public const string Read = "read";
}

public sealed record ConnectorStatusResponse(
    string Version,
    bool Supported,
    IReadOnlyList<string> AvailableKinds,
    IReadOnlyList<string> AvailableCapabilities,
    int MaximumConnectionsPerWorkspace,
    int MaximumResponseBytes,
    int MaximumRedirects,
    bool SecretsEncrypted,
    bool WriteActionsEnabled,
    IReadOnlyList<string> Limitations);

public sealed record ConnectorConnection(
    Guid Id,
    string WorkspaceId,
    string Name,
    string Kind,
    string BaseUrl,
    bool Enabled,
    bool HasCredential,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ConnectorListResponse(
    string WorkspaceId,
    int MaximumConnections,
    IReadOnlyList<ConnectorConnection> Connections);

public sealed record CreateConnectorRequest(
    string Name,
    string Kind,
    string BaseUrl,
    string BearerToken,
    bool Confirmed = false);

public sealed record ConnectorDeleteRequest(
    bool Confirmed = false);

public sealed record ConnectorReadResult(
    Guid ConnectionId,
    string ConnectionName,
    string RequestedPath,
    string FinalUrl,
    int StatusCode,
    string ContentType,
    int ResponseBytes,
    int RedirectCount,
    string Content,
    bool ContentTruncated,
    DateTimeOffset RetrievedAt);

public sealed record ConnectorListToolResult(
    string WorkspaceId,
    int Count,
    IReadOnlyList<ConnectorConnection> Connections);
