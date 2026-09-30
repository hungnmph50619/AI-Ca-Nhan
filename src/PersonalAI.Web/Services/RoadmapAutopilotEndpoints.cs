using PersonalAI.Web.Models;
using PersonalAI.Web.SelfImprovement;

namespace PersonalAI.Web.Services;

public static class RoadmapAutopilotEndpoints
{
    public static IServiceCollection AddRoadmapAutopilot(
        this IServiceCollection services)
    {
        services.AddScoped<IAutopilotProviderRouter, AutopilotProviderRouter>();
        services.AddSingleton<IRoadmapAutopilotCheckpointStore, RoadmapAutopilotCheckpointStore>();
        services.AddScoped<IRoadmapAutopilotService, RoadmapAutopilotService>();
        return services;
    }

    public static IEndpointRouteBuilder MapRoadmapAutopilot(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/development-autopilot/status", (
            IRoadmapAutopilotService autopilot) =>
            Results.Ok(autopilot.GetStatus()));

        endpoints.MapGet("/api/development-autopilot/providers", (
            IAutopilotProviderRouter router) =>
            Results.Ok(router.GetProviders()));

        endpoints.MapGet("/api/development-autopilot/checkpoint", (
            IRoadmapAutopilotCheckpointStore checkpoints) =>
        {
            var checkpoint = checkpoints.Get();
            return checkpoint is null
                ? Results.NotFound()
                : Results.Ok(checkpoint);
        });

        endpoints.MapDelete("/api/development-autopilot/checkpoint", (
            bool confirmed,
            IRoadmapAutopilotCheckpointStore checkpoints,
            IAuditRecorder audit) =>
        {
            if (!confirmed)
                return Results.BadRequest(
                    new ApiError("Cần confirmed=true để xóa checkpoint tự phát triển."));

            checkpoints.Clear();
            audit.Record(
                AuditAgents.User,
                "roadmap-autopilot.checkpoint.clear",
                "development-autopilot:checkpoint",
                "user-confirmed",
                AuditResults.Succeeded);
            return Results.NoContent();
        });

        endpoints.MapGet("/api/development-autopilot/next", (
            IRoadmapAutopilotService autopilot) =>
        {
            var next = autopilot.GetNextVersion();
            return next is null ? Results.NotFound() : Results.Ok(next);
        });

        endpoints.MapPost("/api/development-autopilot/run-next", async (
            RunRoadmapAutopilotRequest request,
            IRoadmapAutopilotService autopilot,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await autopilot.RunNextAsync(
                    request,
                    cancellationToken));
            }
            catch (RoadmapAutopilotValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ToolExecutionInputException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
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
        });

        return endpoints;
    }
}
