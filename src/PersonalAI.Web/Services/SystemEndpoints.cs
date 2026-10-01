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
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

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

            var path = context.Request.Path.Value ?? string.Empty;
            if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith("/api/audit", StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith("/api/system/logs", StringComparison.OrdinalIgnoreCase))
            {
                var elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(
                    startedAt).TotalMilliseconds;
                var statusCode = context.Response.StatusCode;
                var level = statusCode >= 500
                    ? SystemLogLevels.Error
                    : statusCode is 401 or 403
                        ? SystemLogLevels.Security
                        : statusCode >= 400
                            ? SystemLogLevels.Warning
                            : SystemLogLevels.Info;
                var result = statusCode >= 500
                    ? AuditResults.Failed
                    : statusCode is 401 or 403
                        ? AuditResults.Denied
                        : statusCode >= 400
                            ? AuditResults.Failed
                            : AuditResults.Succeeded;

                try
                {
                    var recorder = context.RequestServices
                        .GetRequiredService<IAuditRecorder>();
                    recorder.Record(
                        AuditAgents.System,
                        "http.request",
                        $"{context.Request.Method} {path}",
                        $"status:{statusCode};duration-ms:{elapsedMs:F1}",
                        result,
                        level: level,
                        source: "http",
                        correlationId: requestId);
                }
                catch
                {
                    // Request logging must never change the API response.
                }
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
        services.AddScoped<IUnifiedPermissionService, UnifiedPermissionService>();
        return services;
    }

    public static WebApplication MapStableCore(
        this WebApplication app)
    {
        app.MapGet("/api/system/capabilities", (
            ISystemCoreService core) =>
            Results.Ok(core.GetCapabilities()));

        app.MapGet("/api/system/permissions/status", (
            IUnifiedPermissionService permissions) =>
            Results.Ok(permissions.GetStatus()));

        app.MapGet("/api/system/permissions", (
            IUnifiedPermissionService permissions) =>
            Results.Ok(permissions.GetAll()));

        app.MapPost("/api/system/permissions", (
            SetUnifiedPermissionRequest request,
            IUnifiedPermissionService permissions) =>
        {
            try
            {
                return Results.Ok(permissions.Set(request));
            }
            catch (UnifiedPermissionValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/system/permissions/evaluate", (
            EvaluateUnifiedPermissionRequest request,
            IUnifiedPermissionService permissions) =>
        {
            try
            {
                var decision = permissions.Evaluate(request);
                return decision.Allowed
                    ? Results.Ok(decision)
                    : Results.Json(
                        decision,
                        statusCode: StatusCodes.Status403Forbidden);
            }
            catch (UnifiedPermissionValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapDelete("/api/system/permissions/{ruleId:guid}", (
            Guid ruleId,
            [Microsoft.AspNetCore.Mvc.FromBody] RevokeUnifiedPermissionRequest request,
            IUnifiedPermissionService permissions) =>
        {
            try
            {
                return permissions.Revoke(ruleId, request)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (UnifiedPermissionValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

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
