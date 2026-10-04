using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class UniversalTaskRouterEndpoints
{
    public static IServiceCollection AddUniversalTaskRouter(
        this IServiceCollection services)
    {
        services.AddScoped<IUniversalTaskRouter, UniversalTaskRouter>();
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

        return app;
    }
}
