namespace PersonalAI.Web.Models;

public static class GitCredentialKinds
{
    public const string HttpsToken = "https-token";
    public const string SshPrivateKey = "ssh-private-key";
}

public sealed record CreateGitCredentialRequest(
    string Name,
    string Kind,
    string Secret,
    string? Username = null,
    bool ConfirmStoreCredential = false);

public sealed record DeleteGitCredentialRequest(
    string CredentialRef,
    bool ConfirmDeleteCredential = false);

public sealed record GitCredentialInfo(
    string CredentialRef,
    string WorkspaceId,
    string Name,
    string Kind,
    string? Username,
    bool HasSecret,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GitCredentialStatus(
    string Version,
    string WorkspaceId,
    int Credentials,
    bool SecretsEncrypted,
    bool SecretsReturnedByApi,
    bool PlaintextPersistenceEnabled,
    bool AuditContainsSecrets,
    IReadOnlyList<string> SupportedKinds);

public sealed class GitCredentialValidationException(string message)
    : Exception(message);
