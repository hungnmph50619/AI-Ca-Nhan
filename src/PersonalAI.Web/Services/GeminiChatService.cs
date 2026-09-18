using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PersonalAI.Web.Models;
using PersonalAI.Web.Options;

namespace PersonalAI.Web.Services;

public sealed class GeminiChatService : IAiProvider
{
    private const int MaximumAttempts = 3;
    private const string FunctionPlanningInstructions = """
Tool/function calls in this request are proposals only. Never claim a function has already run.
Choose at most one function. If no function is needed, respond normally and do not call a function.
Only propose a WRITE or DELETE action when the latest user message explicitly asks to change state.
Never treat conversation text as execution confirmation; confirmation is handled separately by the application.
Do not invent function names or arguments outside the supplied schemas.
""";

    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly IAiSettingsStore _settingsStore;
    private readonly ILogger<GeminiChatService> _logger;
    private readonly string _instructions;

    public GeminiChatService(
        HttpClient httpClient,
        IOptions<GeminiOptions> options,
        IAiSettingsStore settingsStore,
        IWebHostEnvironment environment,
        ILogger<GeminiChatService> logger)
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

    public string Name => "Gemini";

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
                "Chưa có Gemini API key. Hãy mở Cài đặt AI để nhập key.");
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

        var requestBody = JsonSerializer.Serialize(payload);
        var model = Uri.EscapeDataString(Model);

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"models/{model}:generateContent")
                {
                    Content = new StringContent(
                        requestBody,
                        Encoding.UTF8,
                        "application/json")
                };
                request.Headers.Add("x-goog-api-key", apiKey);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var detail = ReadApiError(responseBody);
                    if (IsTransientStatusCode(response.StatusCode) && attempt < MaximumAttempts)
                    {
                        var delay = GetRetryDelay(attempt);
                        _logger.LogWarning(
                            "Gemini API returned transient {StatusCode} on attempt {Attempt}/{MaximumAttempts}. Retrying in {DelayMs} ms: {Detail}",
                            (int)response.StatusCode,
                            attempt,
                            MaximumAttempts,
                            delay.TotalMilliseconds,
                            detail);
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

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

                if (attempt > 1)
                {
                    _logger.LogInformation(
                        "Gemini request succeeded on retry attempt {Attempt}/{MaximumAttempts}.",
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
                    "Gemini network request failed on attempt {Attempt}/{MaximumAttempts}. Retrying in {DelayMs} ms.",
                    attempt,
                    MaximumAttempts,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new HttpRequestException("Gemini không thể xử lý yêu cầu sau nhiều lần thử.");
    }


    public async Task<ProviderFunctionCallDecision?> ProposeFunctionCallAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ProviderFunctionDefinition> functions,
        CancellationToken cancellationToken)
    {
        if (functions.Count == 0)
        {
            return null;
        }

        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Chưa có Gemini API key. Hãy mở Cài đặt AI để nhập key.");
        }

        var payload = new
        {
            systemInstruction = new
            {
                parts = new[]
                {
                    new
                    {
                        text = _instructions
                            + Environment.NewLine
                            + Environment.NewLine
                            + FunctionPlanningInstructions
                    }
                }
            },
            contents = messages.Select(message => new
            {
                role = message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                    ? "model"
                    : "user",
                parts = new[] { new { text = message.Content } }
            }),
            tools = new[]
            {
                new
                {
                    functionDeclarations = functions.Select(function => new
                    {
                        name = function.Name,
                        description = function.Description,
                        parameters = SanitizeFunctionSchema(function.Parameters)
                    })
                }
            },
            toolConfig = new
            {
                functionCallingConfig = new
                {
                    mode = "AUTO"
                }
            },
            generationConfig = new
            {
                maxOutputTokens = Math.Clamp(_options.MaxOutputTokens, 128, 16_384)
            }
        };

        var requestBody = JsonSerializer.Serialize(payload);
        var model = Uri.EscapeDataString(Model);

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"models/{model}:generateContent")
                {
                    Content = new StringContent(
                        requestBody,
                        Encoding.UTF8,
                        "application/json")
                };
                request.Headers.Add("x-goog-api-key", apiKey);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var detail = ReadApiError(responseBody);
                    if (IsTransientStatusCode(response.StatusCode) && attempt < MaximumAttempts)
                    {
                        var delay = GetRetryDelay(attempt);
                        _logger.LogWarning(
                            "Gemini native function planning returned transient {StatusCode} on attempt {Attempt}/{MaximumAttempts}. Retrying in {DelayMs} ms: {Detail}",
                            (int)response.StatusCode,
                            attempt,
                            MaximumAttempts,
                            delay.TotalMilliseconds,
                            detail);
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    throw new HttpRequestException(
                        $"Gemini API trả về lỗi {(int)response.StatusCode}: {detail}",
                        null,
                        response.StatusCode);
                }

                return ReadFunctionCall(responseBody);
            }
            catch (HttpRequestException exception) when (
                exception.StatusCode is null
                && attempt < MaximumAttempts
                && !cancellationToken.IsCancellationRequested)
            {
                var delay = GetRetryDelay(attempt);
                _logger.LogWarning(
                    exception,
                    "Gemini native function planning network request failed on attempt {Attempt}/{MaximumAttempts}. Retrying in {DelayMs} ms.",
                    attempt,
                    MaximumAttempts,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new HttpRequestException(
            "Gemini không thể lập đề xuất function call sau nhiều lần thử.");
    }

    private ProviderFunctionCallDecision? ReadFunctionCall(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var calls = new List<ProviderFunctionCallDecision>();
        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (!part.TryGetProperty("functionCall", out var functionCall)
                    || functionCall.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!functionCall.TryGetProperty("name", out var nameElement)
                    || nameElement.ValueKind != JsonValueKind.String)
                {
                    _logger.LogWarning(
                        "Gemini returned a malformed native function call.");
                    return null;
                }

                var name = nameElement.GetString()?.Trim() ?? string.Empty;
                if (name.Length == 0)
                {
                    return null;
                }

                JsonElement arguments;
                if (functionCall.TryGetProperty("args", out var argsElement)
                    && argsElement.ValueKind == JsonValueKind.Object)
                {
                    arguments = argsElement.Clone();
                }
                else
                {
                    using var empty = JsonDocument.Parse("{}");
                    arguments = empty.RootElement.Clone();
                }

                calls.Add(new ProviderFunctionCallDecision(name, arguments));
            }
        }

        if (calls.Count > 1)
        {
            _logger.LogWarning(
                "Gemini returned {CallCount} function calls although PersonalAI only permits one proposal per turn.",
                calls.Count);
            return null;
        }

        return calls.Count == 1 ? calls[0] : null;
    }


    private static JsonElement SanitizeFunctionSchema(JsonElement schema)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteFunctionSchema(schema, writer);
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteFunctionSchema(JsonElement schema, Utf8JsonWriter writer)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            schema.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        foreach (var property in schema.EnumerateObject())
        {
            switch (property.Name)
            {
                case "type":
                case "description":
                case "enum":
                case "required":
                case "minimum":
                case "maximum":
                case "minItems":
                case "maxItems":
                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                    break;
                case "items":
                    writer.WritePropertyName(property.Name);
                    WriteFunctionSchema(property.Value, writer);
                    break;
                case "properties":
                    if (property.Value.ValueKind != JsonValueKind.Object)
                    {
                        break;
                    }

                    writer.WritePropertyName(property.Name);
                    writer.WriteStartObject();
                    foreach (var child in property.Value.EnumerateObject())
                    {
                        writer.WritePropertyName(child.Name);
                        WriteFunctionSchema(child.Value, writer);
                    }
                    writer.WriteEndObject();
                    break;
            }
        }
        writer.WriteEndObject();
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
