using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using PersonalAI.Web.Models;
using PersonalAI.Web.Options;

namespace PersonalAI.Web.Services;

public interface IAiSettingsStore
{
    string ActiveProvider { get; }
    string GetModel(string provider);
    string GetApiKey(string provider);
    AiSettingsResponse GetPublicSettings();
    Task SaveAsync(UpdateAiSettingsRequest request, CancellationToken cancellationToken);
}

public sealed class AiSettingsStore : IAiSettingsStore
{
    private static readonly HashSet<string> SupportedProviders =
        new(StringComparer.OrdinalIgnoreCase) { "Gemini", "OpenAI" };

    private readonly object _gate = new();
    private readonly IDataProtector _protector;
    private readonly ILogger<AiSettingsStore> _logger;
    private readonly string _settingsPath;
    private readonly GeminiOptions _geminiDefaults;
    private readonly OpenAiOptions _openAiDefaults;
    private StoredAiSettings _settings;

    public AiSettingsStore(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<AiOptions> aiOptions,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<OpenAiOptions> openAiOptions,
        ILogger<AiSettingsStore> logger)
    {
        _protector = dataProtectionProvider.CreateProtector("PersonalAI.AiSettings.v1");
        _logger = logger;
        _geminiDefaults = geminiOptions.Value;
        _openAiDefaults = openAiOptions.Value;

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".personalai");
        }

        var settingsDirectory = Path.Combine(localData, "PersonalAI");
        _settingsPath = Path.Combine(settingsDirectory, "ai-settings.json");
        _settings = LoadOrCreateDefaults(aiOptions.Value, settingsDirectory);
    }

    public string ActiveProvider
    {
        get
        {
            lock (_gate)
            {
                return _settings.Provider;
            }
        }
    }

    public string GetModel(string provider)
    {
        lock (_gate)
        {
            return _settings.Models.TryGetValue(NormalizeProvider(provider), out var model)
                ? model
                : GetDefaultModel(provider);
        }
    }

    public string GetApiKey(string provider)
    {
        var normalizedProvider = NormalizeProvider(provider);
        string? encryptedKey;

        lock (_gate)
        {
            _settings.EncryptedApiKeys.TryGetValue(normalizedProvider, out encryptedKey);
        }

        if (!string.IsNullOrWhiteSpace(encryptedKey))
        {
            try
            {
                return _protector.Unprotect(encryptedKey);
            }
            catch (CryptographicException exception)
            {
                _logger.LogWarning(exception, "Could not decrypt the saved {Provider} API key.", normalizedProvider);
            }
        }

        return normalizedProvider == "OpenAI"
            ? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? _openAiDefaults.ApiKey
            : Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? _geminiDefaults.ApiKey;
    }

    public AiSettingsResponse GetPublicSettings()
    {
        var provider = ActiveProvider;
        var apiKey = GetApiKey(provider);
        return new AiSettingsResponse(
            provider,
            GetModel(provider),
            !string.IsNullOrWhiteSpace(apiKey),
            MaskKey(apiKey));
    }

    public async Task SaveAsync(
        UpdateAiSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var providerInput = request.Provider?.Trim() ?? string.Empty;
        if (!SupportedProviders.Contains(providerInput))
        {
            throw new ArgumentException("Nhà cung cấp AI không được hỗ trợ.");
        }

        var provider = NormalizeProvider(providerInput);
        var model = request.Model?.Trim() ?? string.Empty;

        if (model.Length is < 2 or > 120)
        {
            throw new ArgumentException("Tên model phải có từ 2 đến 120 ký tự.");
        }

        var apiKey = request.ApiKey?.Trim();
        if (apiKey?.Length > 512)
        {
            throw new ArgumentException("API key không hợp lệ.");
        }

        StoredAiSettings snapshot;
        lock (_gate)
        {
            _settings.Provider = provider;
            _settings.Models[provider] = model;

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                _settings.EncryptedApiKeys[provider] = _protector.Protect(apiKey);
            }

            snapshot = _settings.Clone();
        }

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        var temporaryPath = $"{_settingsPath}.tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _settingsPath, true);
    }

    private StoredAiSettings LoadOrCreateDefaults(
        AiOptions aiOptions,
        string settingsDirectory)
    {
        Directory.CreateDirectory(settingsDirectory);

        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = File.ReadAllText(_settingsPath);
                var saved = JsonSerializer.Deserialize<StoredAiSettings>(json);
                if (saved is not null && SupportedProviders.Contains(saved.Provider))
                {
                    saved.EnsureDefaults(_geminiDefaults.Model, _openAiDefaults.Model);
                    return saved;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogWarning(exception, "Could not read saved AI settings. Defaults will be used.");
            }
        }

        var provider = SupportedProviders.Contains(aiOptions.Provider)
            ? NormalizeProvider(aiOptions.Provider)
            : "Gemini";

        return new StoredAiSettings
        {
            Provider = provider,
            Models = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Gemini"] = _geminiDefaults.Model,
                ["OpenAI"] = _openAiDefaults.Model
            },
            EncryptedApiKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private string GetDefaultModel(string provider) =>
        NormalizeProvider(provider) == "OpenAI"
            ? _openAiDefaults.Model
            : _geminiDefaults.Model;

    private static string NormalizeProvider(string? provider) =>
        provider?.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) == true
            ? "OpenAI"
            : "Gemini";

    private static string? MaskKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var suffixLength = Math.Min(4, apiKey.Length);
        return $"••••••••{apiKey[^suffixLength..]}";
    }

}

internal sealed class StoredAiSettings
{
    public StoredAiSettings()
    {
    }

    public string Provider { get; set; } = "Gemini";
    public Dictionary<string, string> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> EncryptedApiKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void EnsureDefaults(string geminiModel, string openAiModel)
    {
        Models = new Dictionary<string, string>(
            Models ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
        EncryptedApiKeys = new Dictionary<string, string>(
            EncryptedApiKeys ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
        Models.TryAdd("Gemini", geminiModel);
        Models.TryAdd("OpenAI", openAiModel);
        Provider = Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
            ? "OpenAI"
            : "Gemini";
    }

    public StoredAiSettings Clone() => new()
    {
        Provider = Provider,
        Models = new Dictionary<string, string>(Models, StringComparer.OrdinalIgnoreCase),
        EncryptedApiKeys = new Dictionary<string, string>(EncryptedApiKeys, StringComparer.OrdinalIgnoreCase)
    };
}
