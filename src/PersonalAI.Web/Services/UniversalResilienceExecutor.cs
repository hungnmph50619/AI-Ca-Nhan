using System.Collections.Concurrent;
using System.Net.Sockets;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace PersonalAI.Web.Services;

public static class UniversalFailureActions
{
    public const string RetrySafeRead = "retry-safe-read";
    public const string Wait = "wait";
    public const string Replan = "replan";
    public const string Stop = "stop";
    public const string VerifyBeforeRetry = "verify-before-retry";
    public const string Escalate = "escalate";
}

public sealed record UniversalFailureAssessment(
    string Action,
    bool RetryAllowed,
    bool RequiresVerificationBeforeAnotherSideEffect,
    string Category,
    string Reason);

public interface IUniversalFailureClassifier
{
    UniversalFailureAssessment Classify(
        Exception? exception = null,
        string? detail = null,
        bool sideEffectMayHaveOccurred = false);
}

public sealed class UniversalFailureClassifier
    : IUniversalFailureClassifier
{
    public UniversalFailureAssessment Classify(
        Exception? exception = null,
        string? detail = null,
        bool sideEffectMayHaveOccurred = false)
    {
        var message =
            string.Join(
                    " ",
                    exception?.GetType().Name ?? string.Empty,
                    exception?.Message ?? string.Empty,
                    detail ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        if (exception is OperationCanceledException)
        {
            return new(
                UniversalFailureActions.Stop,
                RetryAllowed: false,
                RequiresVerificationBeforeAnotherSideEffect: false,
                "cancelled",
                "Tác vụ đã bị hủy; không được tự retry.");
        }

        if (exception is ToolExecutionInputException or
            AgentValidationException or
            UnauthorizedAccessException ||
            ContainsAny(
                message,
                "permission denied",
                "access denied",
                "confirmation required",
                "người dùng từ chối",
                "quyền bị từ chối"))
        {
            return new(
                UniversalFailureActions.Stop,
                RetryAllowed: false,
                RequiresVerificationBeforeAnotherSideEffect: false,
                "policy-or-validation",
                "Lỗi policy/validation/quyền không phải lỗi tạm thời; phải dừng hoặc yêu cầu đầu vào mới.");
        }

        if (ContainsAny(
                message,
                "wrong target",
                "target missing",
                "target not found",
                "sai mục tiêu",
                "không còn target",
                "foreground",
                "wrong focus",
                "focus"))
        {
            return new(
                UniversalFailureActions.Replan,
                RetryAllowed: false,
                RequiresVerificationBeforeAnotherSideEffect:
                    sideEffectMayHaveOccurred,
                "context-changed",
                "Ngữ cảnh/target đã thay đổi; retry cùng action không an toàn, cần quan sát lại và replan.");
        }

        if (ContainsAny(
                message,
                "pending",
                "loading",
                "đang tải",
                "đang chờ",
                "progressing"))
        {
            return new(
                UniversalFailureActions.Wait,
                RetryAllowed: false,
                RequiresVerificationBeforeAnotherSideEffect:
                    sideEffectMayHaveOccurred,
                "pending",
                "Trạng thái đang chờ/đang tiến triển; phải wait + verify, không replay action.");
        }

        var transient =
            exception is TimeoutException or
            HttpRequestException or
            IOException or
            SocketException ||
            exception?.GetType().Name.Contains(
                "PlaywrightException",
                StringComparison.OrdinalIgnoreCase) == true ||
            ContainsAny(
                message,
                "timeout",
                "temporarily unavailable",
                "connection reset",
                "connection refused",
                "network unreachable",
                "technical transient");

        if (transient)
        {
            if (sideEffectMayHaveOccurred)
            {
                return new(
                    UniversalFailureActions.VerifyBeforeRetry,
                    RetryAllowed: false,
                    RequiresVerificationBeforeAnotherSideEffect: true,
                    "transient-after-side-effect",
                    "Có lỗi kỹ thuật nhưng side effect có thể đã xảy ra; phải readback/verify trước, tuyệt đối không replay mù.");
            }

            return new(
                UniversalFailureActions.RetrySafeRead,
                RetryAllowed: true,
                RequiresVerificationBeforeAnotherSideEffect: false,
                "transient-technical",
                "Lỗi kỹ thuật tạm thời trên thao tác read-only/idempotent; cho phép retry ngắn có giới hạn.");
        }

        return new(
            UniversalFailureActions.Escalate,
            RetryAllowed: false,
            RequiresVerificationBeforeAnotherSideEffect:
                sideEffectMayHaveOccurred,
            "unknown",
            sideEffectMayHaveOccurred
                ? "Không phân loại chắc chắn và side effect có thể đã xảy ra; phải verify/replan trước action mới."
                : "Không phân loại chắc chắn; không tự retry, chuyển tầng điều phối quyết định.");
    }

    private static bool ContainsAny(
        string value,
        params string[] terms) =>
        terms.Any(term =>
            value.Contains(
                term,
                StringComparison.OrdinalIgnoreCase));
}

public sealed record UniversalResilienceDomainStatus(
    string Domain,
    string CircuitState);

public interface IUniversalResilienceExecutor
{
    ValueTask<T> ExecuteReadOnlyAsync<T>(
        string failureDomain,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default);

    T ExecuteReadOnly<T>(
        string failureDomain,
        Func<T> operation);

    UniversalFailureAssessment Classify(
        Exception? exception = null,
        string? detail = null,
        bool sideEffectMayHaveOccurred = false);

    IReadOnlyList<UniversalResilienceDomainStatus> GetStatus();
}

public sealed class UniversalResilienceExecutor(
    IUniversalFailureClassifier classifier)
    : IUniversalResilienceExecutor
{
    private sealed record DomainPipeline(
        ResiliencePipeline Pipeline,
        CircuitBreakerStateProvider StateProvider);

    private readonly ConcurrentDictionary<string, DomainPipeline>
        pipelines = new(StringComparer.OrdinalIgnoreCase);

    public async ValueTask<T> ExecuteReadOnlyAsync<T>(
        string failureDomain,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var domain =
            NormalizeDomain(
                failureDomain);

        var pipeline =
            pipelines.GetOrAdd(
                domain,
                _ => BuildPipeline());

        return await pipeline.Pipeline.ExecuteAsync(
            operation,
            cancellationToken);
    }

    public T ExecuteReadOnly<T>(
        string failureDomain,
        Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var domain =
            NormalizeDomain(
                failureDomain);

        var pipeline =
            pipelines.GetOrAdd(
                domain,
                _ => BuildPipeline());

        return pipeline.Pipeline.Execute(
            _ => operation(),
            CancellationToken.None);
    }

    public UniversalFailureAssessment Classify(
        Exception? exception = null,
        string? detail = null,
        bool sideEffectMayHaveOccurred = false) =>
        classifier.Classify(
            exception,
            detail,
            sideEffectMayHaveOccurred);

    public IReadOnlyList<UniversalResilienceDomainStatus> GetStatus() =>
        pipelines
            .OrderBy(pair =>
                pair.Key,
                StringComparer.OrdinalIgnoreCase)
            .Select(pair =>
                new UniversalResilienceDomainStatus(
                    pair.Key,
                    pair.Value.StateProvider
                        .CircuitState
                        .ToString()))
            .ToArray();

    private DomainPipeline BuildPipeline()
    {
        var stateProvider =
            new CircuitBreakerStateProvider();

        var pipeline =
            new ResiliencePipelineBuilder()
                .AddCircuitBreaker(
                    new CircuitBreakerStrategyOptions
                    {
                        ShouldHandle =
                            args =>
                                ValueTask.FromResult(
                                    classifier.Classify(
                                            args.Outcome.Exception,
                                            sideEffectMayHaveOccurred: false)
                                        .RetryAllowed),

                        FailureRatio = 0.50,
                        SamplingDuration =
                            TimeSpan.FromSeconds(20),
                        MinimumThroughput = 3,
                        BreakDuration =
                            TimeSpan.FromSeconds(15),
                        StateProvider = stateProvider
                    })
                .AddRetry(
                    new RetryStrategyOptions
                    {
                        ShouldHandle =
                            args =>
                                ValueTask.FromResult(
                                    classifier.Classify(
                                            args.Outcome.Exception,
                                            sideEffectMayHaveOccurred: false)
                                        .RetryAllowed),

                        MaxRetryAttempts = 1,
                        Delay =
                            TimeSpan.FromMilliseconds(150),
                        BackoffType =
                            DelayBackoffType.Exponential,
                        UseJitter = true
                    })
                .Build();

        return new(
            pipeline,
            stateProvider);
    }

    private static string NormalizeDomain(
        string value)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        if (normalized.Length is < 2 or > 120)
            throw new ArgumentException(
                "Failure domain phải từ 2 đến 120 ký tự.",
                nameof(value));

        return normalized;
    }
}
