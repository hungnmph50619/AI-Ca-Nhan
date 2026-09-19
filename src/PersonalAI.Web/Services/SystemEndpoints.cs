using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class SystemHardeningMiddleware
{
    public const string RequestIdHeaderName = "X-PersonalAI-Request-Id";
    public const string ApiVersionHeaderName = "X-PersonalAI-Api-Version";

    public static WebApplication UseSystemHardening(
        this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var requestId = Guid.NewGuid().ToString("N");
            context.TraceIdentifier = requestId;

            context.Response.OnStarting(() =>
            {
                context.Response.Headers[RequestIdHeaderName] = requestId;
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";

                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.Headers[ApiVersionHeaderName] =
                        PersonalAiRelease.ApiContractVersion;
                    context.Response.Headers.CacheControl =
                        "no-store, no-cache, must-revalidate, max-age=0";
                }

                return Task.CompletedTask;
            });

            try
            {
                await next();
            }
            catch (OperationCanceledException) when (
                context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                context.Request.Path.StartsWithSegments("/api"))
            {
                var loggerFactory = context.RequestServices
                    .GetRequiredService<ILoggerFactory>();
                loggerFactory
                    .CreateLogger("PersonalAI.Api")
                    .LogError(
                        exception,
                        "Unhandled API error. RequestId={RequestId} Path={Path}",
                        requestId,
                        context.Request.Path.Value);

                if (context.Response.HasStarted)
                {
                    throw;
                }

                context.Response.Clear();
                context.Response.StatusCode =
                    StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(
                    new ApiError(
                        "Đã xảy ra lỗi nội bộ. Hãy thử lại.",
                        requestId));
            }
        });

        return app;
    }
}

public static class SystemEndpoints
{
    public static IServiceCollection AddStableCore(
        this IServiceCollection services)
    {
        services.AddScoped<ISystemCoreService, SystemCoreService>();
        return services;
    }

    public static WebApplication MapStableCore(
        this WebApplication app)
    {
        app.MapGet("/api/system/capabilities", (
            ISystemCoreService core) =>
            Results.Ok(core.GetCapabilities()));

        app.MapGet("/api/system/health", async (
            ISystemCoreService core,
            CancellationToken cancellationToken) =>
        {
            var health = await core.GetHealthAsync(cancellationToken);
            return health.Ready
                ? Results.Ok(health)
                : Results.Json(
                    health,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        return app;
    }
}
