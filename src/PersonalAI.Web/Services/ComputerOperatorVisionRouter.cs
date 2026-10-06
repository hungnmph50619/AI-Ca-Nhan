using System.Diagnostics;
using System.Net;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class ComputerOperatorVisionRouter(
    IEnumerable<IComputerOperatorVisionProvider> providers,
    IAiSettingsStore settings,
    ILocalVisualProviderHealthRegistry health,
    ILogger<ComputerOperatorVisionRouter> logger)
    : IComputerOperatorVisionRouter
{
    private readonly IReadOnlyList<IComputerOperatorVisionProvider> _providers =
        providers
            .GroupBy(
                provider => provider.Name,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

    private readonly AsyncLocal<ProviderSelection?> _lastSelection =
        new();

    public string Name =>
        _lastSelection.Value?.Name ??
        ResolvePrimary()?.Name ??
        "Unavailable";

    public string Model =>
        _lastSelection.Value?.Model ??
        ResolvePrimary()?.Model ??
        string.Empty;

    public bool Ready =>
        BuildOrder()
            .Any(provider => provider.Ready);

    public IReadOnlyList<ComputerOperatorVisionProviderInfo> GetProviders() =>
        BuildOrder()
            .Select((provider, index) =>
                new ComputerOperatorVisionProviderInfo(
                    provider.Name,
                    provider.Model,
                    provider.Ready,
                    index + 1))
            .ToArray();

    public Task<DesktopVisionTarget> LocateAsync(
        DesktopScreenshotFrame frame,
        string targetDescription,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "locate",
            provider => provider.LocateAsync(
                frame,
                targetDescription,
                cancellationToken),
            cancellationToken);

    public Task<DesktopVisionVerification> VerifyAsync(
        DesktopScreenshotFrame frame,
        string expectedState,
        DesktopFrameDifference? frameDifference,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "verify",
            provider => provider.VerifyAsync(
                frame,
                expectedState,
                frameDifference,
                cancellationToken),
            cancellationToken);

    public Task<DesktopOperatorIntent?> DecideIntentAsync(
        DesktopScreenshotFrame frame,
        string goal,
        string windowsContext,
        string taskHistory,
        CancellationToken cancellationToken) =>
        ExecuteNullableAsync(
            "intent",
            provider => provider.DecideIntentAsync(
                frame,
                goal,
                windowsContext,
                taskHistory,
                cancellationToken),
            cancellationToken);

    public Task<DesktopOperatorDecision> DecideActionAsync(
        DesktopScreenshotFrame frame,
        string goal,
        string windowsContext,
        string taskHistory,
        string temporalSceneContext,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "plan",
            provider => provider.DecideActionAsync(
                frame,
                goal,
                windowsContext,
                taskHistory,
                temporalSceneContext,
                cancellationToken),
            cancellationToken);

    private async Task<T> ExecuteAsync<T>(
        string purpose,
        Func<IComputerOperatorVisionProvider, Task<T>> action,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        var attempted = new List<string>();

        foreach (var provider in BuildOrder())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!provider.Ready)
                continue;

            if (health.ShouldSkip(
                    provider.Name,
                    DateTimeOffset.UtcNow,
                    out var skipReason))
            {
                logger.LogInformation(
                    "Computer Operator provider {Provider} skipped for {Purpose}: {Reason}",
                    provider.Name,
                    purpose,
                    skipReason);
                continue;
            }

            attempted.Add(provider.Name);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result = await action(provider);
                stopwatch.Stop();
                health.RecordSuccess(
                    provider.Name,
                    stopwatch.ElapsedMilliseconds,
                    $"Computer Operator {purpose} thành công.");
                _lastSelection.Value =
                    new ProviderSelection(
                        provider.Name,
                        provider.Model);
                return result;
            }
            catch (HttpRequestException exception)
                when (IsFallbackEligible(exception.StatusCode) &&
                      !cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                lastFailure = exception;
                health.RecordFailure(
                    provider.Name,
                    stopwatch.ElapsedMilliseconds,
                    $"Computer Operator {purpose} lỗi HTTP/network.");
                logger.LogWarning(
                    exception,
                    "Computer Operator provider {Provider} failed transiently for {Purpose}; trying fallback.",
                    provider.Name,
                    purpose);
            }
            catch (TaskCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                lastFailure = exception;
                health.RecordFailure(
                    provider.Name,
                    stopwatch.ElapsedMilliseconds,
                    $"Computer Operator {purpose} timeout.");
                logger.LogWarning(
                    exception,
                    "Computer Operator provider {Provider} timed out for {Purpose}; trying fallback.",
                    provider.Name,
                    purpose);
            }
            catch (InvalidOperationException exception)
            {
                stopwatch.Stop();
                lastFailure = exception;
                health.RecordFailure(
                    provider.Name,
                    stopwatch.ElapsedMilliseconds,
                    $"Computer Operator {purpose} không khả dụng hoặc trả dữ liệu không hợp lệ.");
                logger.LogWarning(
                    exception,
                    "Computer Operator provider {Provider} unavailable for {Purpose}; trying fallback.",
                    provider.Name,
                    purpose);
            }
        }

        if (attempted.Count == 0)
        {
            throw new InvalidOperationException(
                "Chưa có Computer Operator vision provider nào được cấu hình.");
        }

        throw new InvalidOperationException(
            $"Tất cả Computer Operator vision provider đều thất bại cho '{purpose}'. Đã thử: {string.Join(", ", attempted)}.",
            lastFailure);
    }

    private async Task<T?> ExecuteNullableAsync<T>(
        string purpose,
        Func<IComputerOperatorVisionProvider, Task<T?>> action,
        CancellationToken cancellationToken)
        where T : class
    {
        Exception? lastFailure = null;
        var attempted = new List<string>();

        foreach (var provider in BuildOrder())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!provider.Ready)
                continue;

            if (health.ShouldSkip(
                    provider.Name,
                    DateTimeOffset.UtcNow,
                    out var skipReason))
            {
                logger.LogInformation(
                    "Computer Operator provider {Provider} skipped for {Purpose}: {Reason}",
                    provider.Name,
                    purpose,
                    skipReason);
                continue;
            }

            attempted.Add(provider.Name);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result =
                    await action(provider);

                stopwatch.Stop();
                health.RecordSuccess(
                    provider.Name,
                    stopwatch.ElapsedMilliseconds,
                    result is null
                        ? $"Computer Operator {purpose} phản hồi hợp lệ nhưng không có kết quả."
                        : $"Computer Operator {purpose} thành công.");

                if (result is not null)
                {
                    _lastSelection.Value =
                        new ProviderSelection(
                            provider.Name,
                            provider.Model);
                    return result;
                }

                logger.LogInformation(
                    "Computer Operator provider {Provider} returned no result for {Purpose}; trying fallback.",
                    provider.Name,
                    purpose);
            }
            catch (HttpRequestException exception)
                when (IsFallbackEligible(exception.StatusCode) &&
                      !cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                lastFailure = exception;
                health.RecordFailure(
                    provider.Name,
                    stopwatch.ElapsedMilliseconds,
                    $"Computer Operator {purpose} lỗi HTTP/network.");
            }
            catch (TaskCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                lastFailure = exception;
                health.RecordFailure(
                    provider.Name,
                    stopwatch.ElapsedMilliseconds,
                    $"Computer Operator {purpose} timeout.");
            }
            catch (InvalidOperationException exception)
            {
                stopwatch.Stop();
                lastFailure = exception;
                health.RecordFailure(
                    provider.Name,
                    stopwatch.ElapsedMilliseconds,
                    $"Computer Operator {purpose} không khả dụng hoặc trả dữ liệu không hợp lệ.");
            }
        }

        if (attempted.Count == 0)
            return null;

        if (lastFailure is not null)
        {
            logger.LogWarning(
                lastFailure,
                "All Computer Operator providers failed or returned no result for {Purpose}. Attempted: {Providers}.",
                purpose,
                string.Join(", ", attempted));
        }

        return null;
    }

    private sealed record ProviderSelection(
        string Name,
        string Model);

    private IReadOnlyList<IComputerOperatorVisionProvider> BuildOrder()
    {
        var active =
            settings.ActiveProvider?.Trim();

        return _providers
            .OrderBy(provider =>
                provider.Name.Equals(
                    active,
                    StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : provider.Name.Equals(
                        "Gemini",
                        StringComparison.OrdinalIgnoreCase)
                        ? 1
                        : 2)
            .ThenBy(provider =>
                provider.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IComputerOperatorVisionProvider? ResolvePrimary() =>
        BuildOrder()
            .FirstOrDefault(provider =>
                provider.Ready);

    private static bool IsFallbackEligible(
        HttpStatusCode? statusCode) =>
        statusCode is null or
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;
}
