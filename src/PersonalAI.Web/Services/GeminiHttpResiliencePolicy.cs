using System.Net;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace PersonalAI.Web.Services;

public enum GeminiResilienceLane
{
    Reply,
    FunctionPlanning,
    FunctionContinuation
}

/// <summary>
/// Ranh giới resilience dùng chung cho HTTP call tới Gemini, nhưng circuit breaker
/// được cô lập theo lane để lỗi ở chat thường không khóa function planning/continuation.
/// Retry vẫn do GeminiChatService điều khiển để giữ nguyên hành vi/API hiện tại.
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

    private static readonly ResiliencePipeline<HttpResponseMessage> ReplyPipeline =
        BuildPipeline();

    private static readonly ResiliencePipeline<HttpResponseMessage> FunctionPlanningPipeline =
        BuildPipeline();

    private static readonly ResiliencePipeline<HttpResponseMessage> FunctionContinuationPipeline =
        BuildPipeline();

    internal static int IsolatedLaneCountForAcceptance => 3;

    public static async Task<HttpResponseMessage> ExecuteAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            httpClient,
            request,
            GeminiResilienceLane.Reply,
            cancellationToken);

    public static async Task<HttpResponseMessage> ExecuteAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        GeminiResilienceLane lane,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(request);

        var pipeline =
            lane switch
            {
                GeminiResilienceLane.FunctionPlanning =>
                    FunctionPlanningPipeline,
                GeminiResilienceLane.FunctionContinuation =>
                    FunctionContinuationPipeline,
                _ =>
                    ReplyPipeline
            };

        try
        {
            return await pipeline.ExecuteAsync(
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
                $"Gemini lane {lane} đang lỗi lặp lại nên circuit breaker tạm ngừng riêng lane này; các lane khác và Computer Operator local vẫn được phép tiếp tục.",
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

    private static ResiliencePipeline<HttpResponseMessage> BuildPipeline() =>
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
}
