using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class ConnectorEndpoints
{
    public static IServiceCollection AddConnectorFoundation(
        this IServiceCollection services)
    {
        services.AddSingleton<IConnectorService, ConnectorService>();
        services.AddSingleton<IPersonalAiTool, ConnectorsListTool>();
        services.AddSingleton<IPersonalAiTool, ConnectorHttpGetTool>();
        return services;
    }

    public static WebApplication MapConnectorFoundation(
        this WebApplication app)
    {
        app.MapGet("/api/connectors/status", (
            IConnectorService connectors) =>
            Results.Ok(connectors.GetStatus()));

        app.MapGet("/api/connectors", (
            IConnectorService connectors) =>
            Results.Ok(connectors.GetConnections()));

        app.MapPost("/api/connectors", async (
            CreateConnectorRequest request,
            IConnectorService connectors,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(
                    await connectors.CreateAsync(
                        request,
                        cancellationToken));
            }
            catch (ConnectorConfirmationRequiredException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(
                    new ApiError(exception.Message));
            }
        });

        app.MapDelete("/api/connectors/{connectorId:guid}", async (
            Guid connectorId,
            bool? confirmed,
            IConnectorService connectors,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await connectors.DeleteAsync(
                    connectorId,
                    confirmed == true,
                    cancellationToken);
                return Results.NoContent();
            }
            catch (ConnectorConfirmationRequiredException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(
                    new ApiError(exception.Message));
            }
        });

        return app;
    }
}
