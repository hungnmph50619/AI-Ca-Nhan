using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IGitHubWebhookService
{
    GitHubWebhookStatus GetStatus();
    void Configure(ConfigureGitHubWebhookRequest request);
    GitHubWebhookReceiveResult Receive(
        string eventType,
        string deliveryId,
        string signature256,
        ReadOnlyMemory<byte> body);
}

public sealed class GitHubWebhookService : IGitHubWebhookService
{
    public const int MaximumPayloadBytes = 2 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly string _settingsPath;
    private readonly IDataProtector _protector;
    private readonly IWorkspaceContextAccessor _workspace;
    private readonly IDevelopmentEventBus _eventBus;
    private readonly IAuditRecorder _audit;
    private readonly ILogger<GitHubWebhookService> _logger;

    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public GitHubWebhookService(
        IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider,
        IWorkspaceContextAccessor workspace,
        IDevelopmentEventBus eventBus,
        IAuditRecorder audit,
        ILogger<GitHubWebhookService> logger)
    {
        _workspace = workspace;
        _eventBus = eventBus;
        _audit = audit;
        _logger = logger;
        _protector = dataProtectionProvider.CreateProtector(
            "PersonalAI.GitHubWebhook.v1");

        var root = configuration["Development:WebhookRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "Webhook");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        _settingsPath = Path.Combine(root, "github-webhook.json");
    }

    public GitHubWebhookStatus GetStatus()
    {
        var settings = Load();
        return new(
            PersonalAiRelease.Version,
            _workspace.CurrentWorkspaceId,
            Configured: settings is not null,
            SignatureRequired: true,
            SignatureAlgorithm: "hmac-sha256",
            DeliveryIdRequired: true,
            IdempotencyEnabled: true,
            ProcessedDeliveries: _eventBus.GetRecent(500).Count,
            GitHubWebhookEvents.Supported);
    }

    public void Configure(ConfigureGitHubWebhookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmStoreSecret)
        {
            throw new GitHubWebhookValidationException(
                "Cần ConfirmStoreSecret=true để lưu GitHub webhook secret.");
        }

        var secret = (request.Secret ?? string.Empty).Trim();
        if (secret.Length is < 16 or > 4096 ||
            secret.Contains('\r') ||
            secret.Contains('\n'))
        {
            throw new GitHubWebhookValidationException(
                "Webhook secret phải có 16-4096 ký tự và không chứa xuống dòng.");
        }

        var stored = new StoredWebhookSettings
        {
            WorkspaceId = _workspace.CurrentWorkspaceId,
            EncryptedSecret = _protector.Protect(secret),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        lock (_gate)
        {
            var temp = _settingsPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(stored, Options));
            File.Move(temp, _settingsPath, true);
        }

        _audit.Record(
            AuditAgents.User,
            "development.github-webhook.configure",
            "github-webhook:settings",
            "secret:redacted",
            AuditResults.Succeeded);
    }

    public GitHubWebhookReceiveResult Receive(
        string eventType,
        string deliveryId,
        string signature256,
        ReadOnlyMemory<byte> body)
    {
        var normalizedEvent = NormalizeEvent(eventType);
        var normalizedDelivery = NormalizeDelivery(deliveryId);

        if (body.Length is < 2 or > MaximumPayloadBytes)
            throw new GitHubWebhookValidationException(
                $"Webhook payload phải từ 2 bytes đến {MaximumPayloadBytes} bytes.");

        if (_eventBus.HasDelivery(normalizedDelivery))
        {
            return new(
                normalizedDelivery,
                normalizedEvent,
                Accepted: true,
                Duplicate: true,
                EventId: _eventBus.GetRecent(500)
                    .FirstOrDefault(x => x.DeliveryId.Equals(
                        normalizedDelivery,
                        StringComparison.OrdinalIgnoreCase))?.Id,
                DateTimeOffset.UtcNow);
        }

        var secret = ResolveSecret();
        VerifySignature(secret, signature256, body.Span);

        DevelopmentEventEnvelope envelope;
        try
        {
            using var document = JsonDocument.Parse(body);
            envelope = BuildEnvelope(
                normalizedEvent,
                normalizedDelivery,
                document.RootElement);
        }
        catch (JsonException)
        {
            throw new GitHubWebhookValidationException(
                "Webhook payload không phải JSON hợp lệ.");
        }

        var published = _eventBus.Publish(envelope);

        _audit.Record(
            AuditAgents.System,
            "development.github-webhook.receive",
            $"github-delivery:{normalizedDelivery}",
            $"event:{normalizedEvent};repository:{published.Repository};eventId:{published.Id:D}",
            AuditResults.Succeeded);

        return new(
            normalizedDelivery,
            normalizedEvent,
            Accepted: true,
            Duplicate: false,
            published.Id,
            DateTimeOffset.UtcNow);
    }

    private DevelopmentEventEnvelope BuildEnvelope(
        string eventType,
        string deliveryId,
        JsonElement root)
    {
        var repository = ReadString(root, "repository", "full_name");
        if (string.IsNullOrWhiteSpace(repository) ||
            repository.Length > 300)
        {
            throw new GitHubWebhookValidationException(
                "Webhook thiếu repository.full_name hợp lệ.");
        }

        string? action = ReadString(root, "action");
        string? reference = null;
        string? sha = null;
        int? number = null;
        string? status = null;
        string? conclusion = null;
        string? tag = null;

        switch (eventType)
        {
            case GitHubWebhookEvents.Push:
                reference = ReadString(root, "ref");
                sha = ReadString(root, "after");
                break;

            case GitHubWebhookEvents.PullRequest:
                number = ReadInt(root, "number");
                action = ReadString(root, "action");
                sha = ReadString(root, "pull_request", "head", "sha");
                reference = ReadString(root, "pull_request", "head", "ref");
                break;

            case GitHubWebhookEvents.PullRequestReview:
                number = ReadInt(root, "pull_request", "number") ?? ReadInt(root, "number");
                action = ReadString(root, "action");
                status = ReadString(root, "review", "state");
                sha = ReadString(root, "pull_request", "head", "sha");
                break;

            case GitHubWebhookEvents.WorkflowRun:
                action = ReadString(root, "action");
                status = ReadString(root, "workflow_run", "status");
                conclusion = ReadString(root, "workflow_run", "conclusion");
                sha = ReadString(root, "workflow_run", "head_sha");
                reference = ReadString(root, "workflow_run", "head_branch");
                break;

            case GitHubWebhookEvents.CheckRun:
                action = ReadString(root, "action");
                status = ReadString(root, "check_run", "status");
                conclusion = ReadString(root, "check_run", "conclusion");
                sha = ReadString(root, "check_run", "head_sha");
                break;

            case GitHubWebhookEvents.Release:
                action = ReadString(root, "action");
                tag = ReadString(root, "release", "tag_name");
                break;
        }

        return new(
            Guid.NewGuid(),
            _workspace.CurrentWorkspaceId,
            Source: "github",
            eventType,
            deliveryId,
            repository,
            NormalizeOptional(action, 80),
            NormalizeOptional(reference, 300),
            NormalizeOptional(sha, 80),
            number,
            NormalizeOptional(status, 80),
            NormalizeOptional(conclusion, 80),
            NormalizeOptional(tag, 160),
            DateTimeOffset.UtcNow);
    }

    private string ResolveSecret()
    {
        var settings = Load();
        if (settings is null ||
            !settings.WorkspaceId.Equals(
                _workspace.CurrentWorkspaceId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new GitHubWebhookValidationException(
                "GitHub webhook chưa được cấu hình cho workspace hiện tại.");
        }

        try
        {
            return _protector.Unprotect(settings.EncryptedSecret);
        }
        catch (CryptographicException exception)
        {
            _logger.LogWarning(
                exception,
                "Không thể giải mã GitHub webhook secret.");
            throw new GitHubWebhookValidationException(
                "GitHub webhook secret không thể giải mã; hãy cấu hình lại.");
        }
    }

    private StoredWebhookSettings? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_settingsPath)) return null;

            try
            {
                return JsonSerializer.Deserialize<StoredWebhookSettings>(
                    File.ReadAllText(_settingsPath),
                    Options);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                JsonException)
            {
                _logger.LogWarning(
                    exception,
                    "Không thể đọc GitHub webhook settings.");
                return null;
            }
        }
    }

    private static void VerifySignature(
        string secret,
        string? signature256,
        ReadOnlySpan<byte> body)
    {
        var signature = (signature256 ?? string.Empty).Trim();
        const string Prefix = "sha256=";

        if (!signature.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new GitHubWebhookValidationException(
                "Thiếu X-Hub-Signature-256 hợp lệ.");
        }

        var hex = signature[Prefix.Length..];
        if (hex.Length != 64)
        {
            throw new GitHubWebhookValidationException(
                "X-Hub-Signature-256 không hợp lệ.");
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            throw new GitHubWebhookValidationException(
                "X-Hub-Signature-256 không hợp lệ.");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = hmac.ComputeHash(body.ToArray());

        if (!CryptographicOperations.FixedTimeEquals(expected, provided))
        {
            throw new GitHubWebhookValidationException(
                "GitHub webhook signature không hợp lệ.");
        }
    }

    private static string NormalizeEvent(string? value)
    {
        var eventType = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (!GitHubWebhookEvents.Supported.Contains(
                eventType,
                StringComparer.Ordinal))
        {
            throw new GitHubWebhookValidationException(
                "GitHub webhook event chưa được hỗ trợ.");
        }
        return eventType;
    }

    private static string NormalizeDelivery(string? value)
    {
        var delivery = (value ?? string.Empty).Trim();
        if (delivery.Length is < 1 or > 120 ||
            delivery.Any(c =>
                !(char.IsLetterOrDigit(c) || c is '-' or '_')))
        {
            throw new GitHubWebhookValidationException(
                "X-GitHub-Delivery không hợp lệ.");
        }
        return delivery;
    }

    private static string? NormalizeOptional(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= maximum
            ? normalized
            : normalized[..maximum];
    }

    private static string? ReadString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out current))
                return null;
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString()
            : null;
    }

    private static int? ReadInt(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out current))
                return null;
        }

        return current.ValueKind == JsonValueKind.Number &&
               current.TryGetInt32(out var value)
            ? value
            : null;
    }

    private sealed class StoredWebhookSettings
    {
        public string WorkspaceId { get; set; } = string.Empty;
        public string EncryptedSecret { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
