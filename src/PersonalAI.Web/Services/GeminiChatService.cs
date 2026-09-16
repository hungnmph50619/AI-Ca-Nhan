using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PersonalAI.Web.Models;
using PersonalAI.Web.Options;

namespace PersonalAI.Web.Services;

public sealed class GeminiChatService : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiChatService> _logger;
    private readonly string _instructions;

    public GeminiChatService(
        HttpClient httpClient,
        IOptions<GeminiOptions> options,
        IWebHostEnvironment environment,
        ILogger<GeminiChatService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        var constitutionPath = Path.Combine(environment.ContentRootPath, "AI-CONSTITUTION.md");
        _instructions = File.Exists(constitutionPath)
            ? File.ReadAllText(constitutionPath)
            : "Bạn là trợ lý AI hữu ích, trung thực và không bịa thông tin.";
    }

    public string Name => "Gemini";

    public string Model => _options.Model;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(GetApiKey());

    public async Task<string> ReplyAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Chưa cấu hình GEMINI_API_KEY. Hãy xem hướng dẫn trong README.md.");
        }

        var payload = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = _instructions } }
            },
            contents = messages.Select(message => new
            {
                role = message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                    ? "model"
                    : "user",
                parts = new[] { new { text = message.Content } }
            }),
            generationConfig = new
            {
                maxOutputTokens = Math.Clamp(_options.MaxOutputTokens, 128, 16_384)
            }
        };

        var model = Uri.EscapeDataString(_options.Model);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"models/{model}:generateContent")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = ReadApiError(responseBody);
            _logger.LogWarning(
                "Gemini API returned {StatusCode}: {Detail}",
                (int)response.StatusCode,
                detail);
            throw new HttpRequestException(
                $"Gemini API trả về lỗi {(int)response.StatusCode}: {detail}",
                null,
                response.StatusCode);
        }

        var outputText = ReadOutputText(responseBody);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            throw new InvalidOperationException(
                "Gemini không trả về nội dung văn bản. Yêu cầu có thể đã bị chặn.");
        }

        return outputText;
    }

    private string GetApiKey() =>
        Environment.GetEnvironmentVariable("GEMINI_API_KEY")
        ?? _options.ApiKey;

    private static string ReadOutputText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var contentParts)
                || contentParts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in contentParts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    parts.Add(text.GetString() ?? string.Empty);
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
            // Upstream did not return JSON. Use the safe generic message below.
        }

        return "Không thể xử lý yêu cầu từ dịch vụ AI.";
    }
}
