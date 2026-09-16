using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PersonalAI.Web.Models;
using PersonalAI.Web.Options;

namespace PersonalAI.Web.Services;

public sealed class OpenAiChatService : IAiProvider
{
    private const int MaximumAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly IAiSettingsStore _settingsStore;
    private readonly ILogger<OpenAiChatService> _logger;
    private readonly string _instructions;

    public OpenAiChatService(
        HttpClient httpClient,
        IOptions<OpenAiOptions> options,
        IAiSettingsStore settingsStore,
        IWebHostEnvironment environment,
        ILogger<OpenAiChatService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _settingsStore = settingsStore;
        _logger = logger;

        var constitutionPath = Path.Combine(environment.ContentRootPath, "AI-CONSTITUTION.md");
        _instructions = File.Exists(constitutionPath)
            ? File.ReadAllText(constitutionPath)
            : "Bạn là trợ lý AI hữu ích, trung thực và không bịa thông tin.";
    }

    public string Name => "OpenAI";

    public string Model => _settingsStore.GetModel(Name);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(GetApiKey());

    public async Task<string> ReplyAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Chưa có OpenAI API key. Hãy mở Cài đặt AI để nhập key.");
        }

        var payload = new
        {
            model = Model,
            instructions = _instructions,
            input = messages.Select(message => new
            {
                role = message.Role,
                content = message.Content
            }),
            max_output_tokens = Math.Clamp(_options.MaxOutputTokens, 128, 16_384),
            store = false
        };

        var requestBody = JsonSerializer.Serialize(payload);

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "responses")
                {
                    Content = new StringContent(
                        requestBody,
                        Encoding.UTF8,
                        "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var detail = ReadApiError(responseBody);
                    if (IsTransientStatusCode(response.StatusCode) && attempt < MaximumAttempts)
                    {
                        var delay = GetRetryDelay(attempt);
                        _logger.LogWarning(
                            "OpenAI API returned transient {StatusCode} on attempt {Attempt}/{MaximumAttempts}. Retrying in {DelayMs} ms: {Detail}",
                            (int)response.StatusCode,
                            attempt,
                            MaximumAttempts,
                            delay.TotalMilliseconds,
                            detail);
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    _logger.LogWarning(
                        "OpenAI API returned {StatusCode}: {Detail}",
                        (int)response.StatusCode,
                        detail);
                    throw new HttpRequestException(
                        $"OpenAI API trả về lỗi {(int)response.StatusCode}: {detail}",
                        null,
                        response.StatusCode);
                }

                var outputText = ReadOutputText(responseBody);
                if (string.IsNullOrWhiteSpace(outputText))
                {
                    throw new InvalidOperationException("API không trả về nội dung văn bản.");
                }

                if (attempt > 1)
                {
                    _logger.LogInformation(
                        "OpenAI request succeeded on retry attempt {Attempt}/{MaximumAttempts}.",
                        attempt,
                        MaximumAttempts);
                }

                return outputText;
            }
            catch (HttpRequestException exception) when (
                exception.StatusCode is null
                && attempt < MaximumAttempts
                && !cancellationToken.IsCancellationRequested)
            {
                var delay = GetRetryDelay(attempt);
                _logger.LogWarning(
                    exception,
                    "OpenAI network request failed on attempt {Attempt}/{MaximumAttempts}. Retrying in {DelayMs} ms.",
                    attempt,
                    MaximumAttempts,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new HttpRequestException("OpenAI không thể xử lý yêu cầu sau nhiều lần thử.");
    }

    private string GetApiKey() => _settingsStore.GetApiKey(Name);

    private static bool IsTransientStatusCode(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static TimeSpan GetRetryDelay(int failedAttempt) =>
        TimeSpan.FromSeconds(Math.Pow(2, failedAttempt - 1));

    private static string ReadOutputText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (root.TryGetProperty("output_text", out var directText)
            && directText.ValueKind == JsonValueKind.String)
        {
            return directText.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output)
            || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    parts.Add(text.GetString() ?? string.Empty);
                }
                else if (part.TryGetProperty("refusal", out var refusal)
                         && refusal.ValueKind == JsonValueKind.String)
                {
                    parts.Add(refusal.GetString() ?? string.Empty);
                }
            }
        }

        return string.Join(Environment.NewLine, parts.Where(part => part.Length > 0));
    }

    private static string ReadApiError(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? "Lỗi không xác định";
            }
        }
        catch (JsonException)
        {
            // The upstream response was not JSON; use a generic safe message below.
        }

        return "Không thể xử lý yêu cầu từ dịch vụ AI.";
    }
}
