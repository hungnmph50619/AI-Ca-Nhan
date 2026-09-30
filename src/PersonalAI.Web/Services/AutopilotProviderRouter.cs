using System.Net;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record AutopilotProviderInfo(
    string Name,
    string Model,
    bool Configured,
    int Priority);

public sealed record AutopilotProviderReply(
    string Content,
    string Provider,
    string Model,
    IReadOnlyList<string> AttemptedProviders);

public interface IAutopilotProviderRouter
{
    IReadOnlyList<AutopilotProviderInfo> GetProviders();
    Task<AutopilotProviderReply> ReplyAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);
}

public sealed class AutopilotProviderRouter(
    IAiProviderResolver providers,
    IAiSettingsStore settings,
    ILogger<AutopilotProviderRouter> logger) : IAutopilotProviderRouter
{
    public IReadOnlyList<AutopilotProviderInfo> GetProviders()
    {
        var order = BuildOrder();
        return order
            .Select((name, index) =>
            {
                var provider = providers.GetByName(name);
                return new AutopilotProviderInfo(
                    provider.Name,
                    provider.Model,
                    provider.IsConfigured,
                    index + 1);
            })
            .ToArray();
    }

    public async Task<AutopilotProviderReply> ReplyAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var attempted = new List<string>();
        Exception? lastFailure = null;

        foreach (var name in BuildOrder())
        {
            cancellationToken.ThrowIfCancellationRequested();

            IAiProvider provider;
            try
            {
                provider = providers.GetByName(name);
            }
            catch (InvalidOperationException exception)
            {
                lastFailure = exception;
                continue;
            }

            if (!provider.IsConfigured)
                continue;

            attempted.Add(provider.Name);
            try
            {
                var content = await provider.ReplyAsync(messages, cancellationToken);
                return new AutopilotProviderReply(
                    content,
                    provider.Name,
                    provider.Model,
                    attempted.ToArray());
            }
            catch (HttpRequestException exception) when (
                IsFallbackEligible(exception.StatusCode) &&
                !cancellationToken.IsCancellationRequested)
            {
                lastFailure = exception;
                logger.LogWarning(
                    "Autopilot provider {Provider} unavailable ({StatusCode}); trying fallback.",
                    provider.Name,
                    exception.StatusCode);
            }
            catch (InvalidOperationException exception)
            {
                lastFailure = exception;
                logger.LogWarning(
                    "Autopilot provider {Provider} unavailable; trying fallback.",
                    provider.Name);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = new TimeoutException(
                    $"Provider {provider.Name} timed out.");
                logger.LogWarning(
                    "Autopilot provider {Provider} timed out; trying fallback.",
                    provider.Name);
            }
        }

        if (attempted.Count == 0)
            throw new InvalidOperationException(
                "Chưa có nhà cung cấp AI nào được cấu hình cho chế độ tự phát triển.");

        throw new InvalidOperationException(
            $"Tất cả nhà cung cấp AI khả dụng đều thất bại trong lượt này. Đã thử: {string.Join(", ", attempted)}. Tiến độ có thể được tiếp tục ở lần chạy sau.",
            lastFailure);
    }

    private IReadOnlyList<string> BuildOrder()
    {
        var active = settings.ActiveProvider?.Trim();
        var result = new List<string>();

        if (string.Equals(active, "OpenAI", StringComparison.OrdinalIgnoreCase))
            result.Add("OpenAI");
        else if (string.Equals(active, "Gemini", StringComparison.OrdinalIgnoreCase))
            result.Add("Gemini");

        foreach (var fallback in new[] { "OpenAI", "Gemini" })
        {
            if (!result.Contains(fallback, StringComparer.OrdinalIgnoreCase))
                result.Add(fallback);
        }

        return result;
    }

    private static bool IsFallbackEligible(HttpStatusCode? statusCode) =>
        statusCode is null or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;
}
