using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class DecisionEngineEndpoints
{
    public static IServiceCollection AddDecisionEngine(
        this IServiceCollection services)
    {
        services.AddScoped<IDecisionEngineService, DecisionEngineService>();
        return services;
    }

    public static WebApplication MapDecisionEngine(
        this WebApplication app)
    {
        app.MapGet(
            "/api/decisions/status",
            (IDecisionEngineService decisions) =>
                Results.Ok(decisions.GetStatus()));

        app.MapPost(
            "/api/decisions/preview",
            async (
                DecisionAnalyzeRequest request,
                IDecisionEngineService decisions,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(
                        await decisions.PreviewAsync(
                            request,
                            cancellationToken));
                }
                catch (DecisionValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (KnowledgeDocumentValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
            });

        app.MapPost(
            "/api/decisions/analyze",
            async (
                DecisionAnalyzeRequest request,
                IDecisionEngineService decisions,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(
                        await decisions.AnalyzeAsync(
                            request,
                            cancellationToken));
                }
                catch (DecisionValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (KnowledgeDocumentValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode:
                            StatusCodes.Status503ServiceUnavailable);
                }
                catch (HttpRequestException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode:
                            StatusCodes.Status502BadGateway);
                }
                catch (TaskCanceledException) when (
                    !cancellationToken.IsCancellationRequested)
                {
                    return Results.Json(
                        new ApiError(
                            "Decision Engine mất quá nhiều thời gian. Hãy thử lại."),
                        statusCode:
                            StatusCodes.Status504GatewayTimeout);
                }
            });

        return app;
    }
}
