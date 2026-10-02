using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record GitCredentialMaterial(
    string CredentialRef,
    string Kind,
    string? Username,
    string Secret);

public interface IGitCredentialService
{
    GitCredentialStatus GetStatus();
    IReadOnlyList<GitCredentialInfo> GetAll();
    GitCredentialInfo Create(CreateGitCredentialRequest request);
    void Delete(DeleteGitCredentialRequest request);
    GitCredentialMaterial Resolve(string credentialRef);
}

public sealed class GitCredentialService : IGitCredentialService
{
    public const int MaximumCredentialsPerWorkspace = 20;
    public const int MaximumSecretCharacters = 16_384;

    private readonly object _gate = new();
    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly IWorkspaceContextAccessor _workspace;
    private readonly IAuditRecorder _audit;
    private readonly ILogger<GitCredentialService> _logger;

    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public GitCredentialService(
        IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider,
        IWorkspaceContextAccessor workspace,
        IAuditRecorder audit,
        ILogger<GitCredentialService> logger)
    {
        _workspace = workspace;
        _audit = audit;
        _logger = logger;
        _protector = dataProtectionProvider.CreateProtector(
            "PersonalAI.GitCredentials.v1");

        var root = configuration["Development:CredentialRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "Credentials");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "git-credentials.json");
    }

    public GitCredentialStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            _workspace.CurrentWorkspaceId,
            all.Count,
            SecretsEncrypted: true,
            SecretsReturnedByApi: false,
            PlaintextPersistenceEnabled: false,
            AuditContainsSecrets: false,
            [GitCredentialKinds.HttpsToken, GitCredentialKinds.SshPrivateKey]);
    }

    public IReadOnlyList<GitCredentialInfo> GetAll()
    {
        lock (_gate)
        {
            var workspaceId = _workspace.CurrentWorkspaceId;
            return Load()
                .Where(x => x.WorkspaceId.Equals(
                    workspaceId,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToPublic)
                .ToArray();
        }
    }

    public GitCredentialInfo Create(CreateGitCredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmStoreCredential)
        {
            throw new GitCredentialValidationException(
                "Cần ConfirmStoreCredential=true để lưu Git credential.");
        }

        var workspaceId = _workspace.CurrentWorkspaceId;
        var name = NormalizeName(request.Name);
        var kind = NormalizeKind(request.Kind);
        var secret = NormalizeSecret(request.Secret, kind);
        var username = NormalizeUsername(request.Username);
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            var all = Load();
            if (all.Count(x => x.WorkspaceId.Equals(
                    workspaceId,
                    StringComparison.OrdinalIgnoreCase)) >=
                MaximumCredentialsPerWorkspace)
            {
                throw new GitCredentialValidationException(
                    $"Workspace đã đạt giới hạn {MaximumCredentialsPerWorkspace} Git credential.");
            }

            if (all.Any(x =>
                x.WorkspaceId.Equals(workspaceId, StringComparison.OrdinalIgnoreCase) &&
                x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new GitCredentialValidationException(
                    "Tên Git credential đã tồn tại trong workspace.");
            }

            var stored = new StoredGitCredential
            {
                CredentialRef = $"gitcred_{Guid.NewGuid():N}",
                WorkspaceId = workspaceId,
                Name = name,
                Kind = kind,
                Username = username,
                EncryptedSecret = _protector.Protect(secret),
                CreatedAt = now,
                UpdatedAt = now
            };

            all.Add(stored);
            Save(all);

            _audit.Record(
                AuditAgents.User,
                "development.git-credential.create",
                $"git-credential:{stored.CredentialRef}",
                $"kind:{kind};name:{name};secret:redacted",
                AuditResults.Succeeded);

            return ToPublic(stored);
        }
    }

    public void Delete(DeleteGitCredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmDeleteCredential)
        {
            throw new GitCredentialValidationException(
                "Cần ConfirmDeleteCredential=true để xóa Git credential.");
        }

        var credentialRef = NormalizeCredentialRef(request.CredentialRef);
        var workspaceId = _workspace.CurrentWorkspaceId;

        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x =>
                x.WorkspaceId.Equals(workspaceId, StringComparison.OrdinalIgnoreCase) &&
                x.CredentialRef.Equals(credentialRef, StringComparison.Ordinal));

            if (index < 0)
                throw new KeyNotFoundException("Không tìm thấy Git credential.");

            var removed = all[index];
            all.RemoveAt(index);
            Save(all);

            _audit.Record(
                AuditAgents.User,
                "development.git-credential.delete",
                $"git-credential:{removed.CredentialRef}",
                $"kind:{removed.Kind};name:{removed.Name};secret:redacted",
                AuditResults.Succeeded);
        }
    }

    public GitCredentialMaterial Resolve(string credentialRef)
    {
        credentialRef = NormalizeCredentialRef(credentialRef);
        var workspaceId = _workspace.CurrentWorkspaceId;

        StoredGitCredential stored;
        lock (_gate)
        {
            stored = Load().FirstOrDefault(x =>
                x.WorkspaceId.Equals(workspaceId, StringComparison.OrdinalIgnoreCase) &&
                x.CredentialRef.Equals(credentialRef, StringComparison.Ordinal))
                ?? throw new KeyNotFoundException("Không tìm thấy Git credential.");
        }

        try
        {
            var secret = _protector.Unprotect(stored.EncryptedSecret);
            return new(
                stored.CredentialRef,
                stored.Kind,
                stored.Username,
                secret);
        }
        catch (CryptographicException exception)
        {
            _logger.LogWarning(
                exception,
                "Không thể giải mã Git credential {CredentialRef}.",
                stored.CredentialRef);
            throw new GitCredentialValidationException(
                "Git credential không thể giải mã; hãy cấu hình lại credential.");
        }
    }

    private List<StoredGitCredential> Load()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<StoredGitCredential>>(
                File.ReadAllText(_path),
                Options) ?? [];
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            _logger.LogWarning(
                exception,
                "Không thể đọc Git credential store; dùng danh sách trống.");
            return [];
        }
    }

    private void Save(List<StoredGitCredential> items)
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(items, Options));
        File.Move(temp, _path, true);
    }

    private static GitCredentialInfo ToPublic(StoredGitCredential item) =>
        new(
            item.CredentialRef,
            item.WorkspaceId,
            item.Name,
            item.Kind,
            item.Username,
            HasSecret: !string.IsNullOrWhiteSpace(item.EncryptedSecret),
            item.CreatedAt,
            item.UpdatedAt);

    private static string NormalizeName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length is < 2 or > 80 || name.Any(char.IsControl))
            throw new GitCredentialValidationException(
                "Tên credential phải có từ 2 đến 80 ký tự.");
        return name;
    }

    private static string NormalizeKind(string? value)
    {
        var kind = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (kind is not (
            GitCredentialKinds.HttpsToken or
            GitCredentialKinds.SshPrivateKey))
        {
            throw new GitCredentialValidationException(
                "Kind phải là https-token hoặc ssh-private-key.");
        }
        return kind;
    }

    private static string NormalizeSecret(string? value, string kind)
    {
        var secret = (value ?? string.Empty).Trim();
        if (secret.Length is < 4 or > MaximumSecretCharacters)
            throw new GitCredentialValidationException(
                "Secret credential không hợp lệ.");

        if (kind == GitCredentialKinds.HttpsToken &&
            (secret.Contains('\r') || secret.Contains('\n')))
        {
            throw new GitCredentialValidationException(
                "HTTPS token không được chứa xuống dòng.");
        }

        if (kind == GitCredentialKinds.SshPrivateKey &&
            !secret.Contains("PRIVATE KEY", StringComparison.Ordinal))
        {
            throw new GitCredentialValidationException(
                "SSH private key không có định dạng được nhận diện.");
        }

        return secret;
    }

    private static string? NormalizeUsername(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var username = value.Trim();
        if (username.Length > 120 ||
            username.Contains('\r') ||
            username.Contains('\n') ||
            username.Any(char.IsControl))
        {
            throw new GitCredentialValidationException(
                "Username credential không hợp lệ.");
        }
        return username;
    }

    private static string NormalizeCredentialRef(string? value)
    {
        var reference = (value ?? string.Empty).Trim();
        if (!reference.StartsWith("gitcred_", StringComparison.Ordinal) ||
            reference.Length is < 20 or > 80 ||
            reference.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
        {
            throw new GitCredentialValidationException(
                "CredentialRef không hợp lệ.");
        }
        return reference;
    }

    private sealed class StoredGitCredential
    {
        public string CredentialRef { get; set; } = string.Empty;
        public string WorkspaceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string EncryptedSecret { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
