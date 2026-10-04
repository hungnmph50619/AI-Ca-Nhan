using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class UniversalTaskRouterEndpoints
{
    public static IServiceCollection AddUniversalTaskRouter(
        this IServiceCollection services)
    {
        services.AddScoped<IUniversalTaskRouter, UniversalTaskRouter>();
        services.AddScoped<IUniversalDirectToolPath, UniversalDirectToolPath>();
        services.AddSingleton<IUniversalFallbackPolicy, UniversalFallbackPolicy>();
        services.AddSingleton<IUniversalOutcomeVerificationService, UniversalOutcomeVerificationService>();
        services.AddSingleton<IUniversalVerificationEvidenceRouter, UniversalVerificationEvidenceRouter>();
        services.AddSingleton<IUniversalVerificationEvidenceAdapters, UniversalVerificationEvidenceAdapters>();
        services.AddSingleton<IUniversalVerificationPipeline, UniversalVerificationPipeline>();
        return services;
    }

    public static WebApplication MapUniversalTaskRouter(
        this WebApplication app)
    {
        app.MapPost("/api/universal-router/preview", (
            UniversalTaskRouteRequest request,
            IUniversalTaskRouter router) =>
        {
            try
            {
                return Results.Ok(
                    router.Preview(request));
            }
            catch (AgentValidationException exception)
            {
                return Results.BadRequest(
                    new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/universal-router/execute", async (
            UniversalTaskRouteRequest request,
            IUniversalTaskRouter router,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(
                    await router.ExecuteAsync(
                        request,
                        cancellationToken));
            }
            catch (AgentValidationException exception)
            {
                return Results.BadRequest(
                    new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/universal-router/prepare-execution", async (
            UniversalExecutionPrepareRequest request,
            IUniversalDirectToolPath directToolPath,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(
                    await directToolPath.PrepareAsync(
                        request,
                        cancellationToken));
            }
            catch (ToolProposalValidationException exception)
            {
                return Results.BadRequest(
                    new ApiError(exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (HttpRequestException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (TaskCanceledException) when (
                !cancellationToken.IsCancellationRequested)
            {
                return Results.Json(
                    new ApiError(
                        "Chuẩn bị direct tool mất quá nhiều thời gian. Hãy thử lại."),
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
        });

        app.MapPost("/api/universal-router/verify-tool-outcome", (
            UniversalToolOutcomeVerificationRequest request,
            IUniversalOutcomeVerificationService verification) =>
            Results.Ok(
                verification.VerifyTool(request)));

        app.MapPost("/api/universal-router/verify-agent-outcome", (
            UniversalAgentOutcomeVerificationRequest request,
            IUniversalOutcomeVerificationService verification) =>
            Results.Ok(
                verification.VerifyAgent(request)));

        app.MapPost("/api/universal-router/route-verification-evidence", (
            UniversalVerificationEvidenceRouteRequest request,
            IUniversalVerificationEvidenceRouter router) =>
            Results.Ok(
                router.Route(request)));

        app.MapPost("/api/universal-router/verify-pipeline", (
            UniversalVerificationPipelineRequest request,
            IUniversalVerificationPipeline pipeline) =>
            Results.Ok(
                pipeline.Verify(request)));

        app.MapPost("/api/universal-router/evaluate-tool-outcome", (
            UniversalFallbackEvaluateRequest request,
            IUniversalFallbackPolicy fallbackPolicy) =>
            Results.Ok(
                fallbackPolicy.Evaluate(
                    request.Route,
                    request.Execution,
                    request.Verification)));

        return app;
    }
}
