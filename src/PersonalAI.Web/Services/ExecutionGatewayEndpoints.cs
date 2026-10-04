namespace PersonalAI.Web.Services;

public static class ExecutionGatewayEndpoints
{
    public static IServiceCollection AddExecutionGateway(
        this IServiceCollection services)
    {
        services.AddScoped<IExecutionGateway, ExecutionGateway>();
        return services;
    }

    public static WebApplication MapExecutionGateway(
        this WebApplication app)
    {
        app.MapPost("/api/execution-gateway/preview", (
            ExecutionGatewayRequest request,
            IExecutionGateway gateway) =>
        {
            try
            {
                return Results.Ok(
                    gateway.Preview(request));
            }
            catch (AgentValidationException exception)
            {
                return Results.BadRequest(
                    new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/execution-gateway/execute", async (
            ExecutionGatewayRequest request,
            IExecutionGateway gateway,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(
                    await gateway.ExecuteAsync(
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
