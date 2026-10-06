using System.Net;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace PersonalAI.Web.Services;

/// <summary>
/// Ranh giới resilience dùng chung cho mọi HTTP call tới Gemini.
/// Retry vẫn do GeminiChatService điều khiển để giữ nguyên hành vi/API hiện tại.
/// Polly chỉ bổ sung timeout cứng và circuit breaker để không treo hoặc đập dịch vụ ngoài khi đang lỗi.
/// </summary>
public static class GeminiHttpResiliencePolicy
{
    public static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(45);

    public static readonly TimeSpan SamplingDuration =
        TimeSpan.FromSeconds(30);

    public static readonly TimeSpan BreakDuration =
        TimeSpan.FromSeconds(20);

    public const int MinimumThroughput = 4;
    public const double FailureRatio = 0.5d;

    private static readonly ResiliencePipeline<HttpResponseMessage> Pipeline =
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddTimeout(RequestTimeout)
            .AddCircuitBreaker(
                new CircuitBreakerStrategyOptions<HttpResponseMessage>
                {
                    ShouldHandle =
                        new PredicateBuilder<HttpResponseMessage>()
                            .Handle<HttpRequestException>()
                            .HandleResult(
                                static response =>
                                    IsTransientStatusCode(
                                        response.StatusCode)),
                    FailureRatio = FailureRatio,
                    SamplingDuration = SamplingDuration,
                    MinimumThroughput = MinimumThroughput,
                    BreakDuration = BreakDuration
                })
            .Build();

    public static async Task<HttpResponseMessage> ExecuteAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await Pipeline.ExecuteAsync(
                async resilienceToken =>
                    await httpClient.SendAsync(
                        request,
                        resilienceToken),
                cancellationToken);
        }
        catch (TimeoutRejectedException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException(
                $"Gemini không phản hồi trong giới hạn {RequestTimeout.TotalSeconds:0} giây.",
                exception);
        }
        catch (BrokenCircuitException exception)
        {
            throw new HttpRequestException(
                "Gemini đang lỗi lặp lại nên circuit breaker tạm ngừng gọi dịch vụ để Computer Operator có thể chờ/replan an toàn.",
                exception,
                HttpStatusCode.ServiceUnavailable);
        }
    }

    public static bool IsTransientStatusCode(
        HttpStatusCode statusCode) =>
        statusCode is
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;
}
