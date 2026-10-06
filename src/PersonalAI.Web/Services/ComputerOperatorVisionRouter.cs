using System.Net;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class ComputerOperatorVisionRouter(
    IEnumerable<IComputerOperatorVisionProvider> providers,
    IAiSettingsStore settings,
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

    public string Name =>
        ResolvePrimary()?.Name ??
        "Unavailable";

    public string Model =>
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

            attempted.Add(provider.Name);

            try
            {
                return await action(provider);
            }
            catch (HttpRequestException exception)
                when (IsFallbackEligible(exception.StatusCode) &&
                      !cancellationToken.IsCancellationRequested)
            {
                lastFailure = exception;
                logger.LogWarning(
                    exception,
                    "Computer Operator provider {Provider} failed transiently for {Purpose}; trying fallback.",
                    provider.Name,
                    purpose);
            }
            catch (TaskCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = exception;
                logger.LogWarning(
                    exception,
                    "Computer Operator provider {Provider} timed out for {Purpose}; trying fallback.",
                    provider.Name,
                    purpose);
            }
            catch (InvalidOperationException exception)
            {
                lastFailure = exception;
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

            attempted.Add(provider.Name);

            try
            {
                var result =
                    await action(provider);

                if (result is not null)
                    return result;

                logger.LogInformation(
                    "Computer Operator provider {Provider} returned no result for {Purpose}; trying fallback.",
                    provider.Name,
                    purpose);
            }
            catch (HttpRequestException exception)
                when (IsFallbackEligible(exception.StatusCode) &&
                      !cancellationToken.IsCancellationRequested)
            {
                lastFailure = exception;
            }
            catch (TaskCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = exception;
            }
            catch (InvalidOperationException exception)
            {
                lastFailure = exception;
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
