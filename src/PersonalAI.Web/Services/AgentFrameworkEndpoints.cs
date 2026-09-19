using System.Net;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class AgentFrameworkEndpoints
{
    public static IServiceCollection AddAgentFramework(
        this IServiceCollection services)
    {
        services.AddScoped<IAgent, PersonalAssistantFrameworkAgent>();
        services.AddScoped<IAgent, PlannerFrameworkAgent>();
        services.AddScoped<IAgent, ResearchFrameworkAgent>();
        services.AddScoped<IAgent, DeveloperFrameworkAgent>();
        services.AddScoped<IAgent, OfficeFrameworkAgent>();
        services.AddScoped<IAgent, OperatorFrameworkAgent>();
        services.AddScoped<IAgent, ReviewerFrameworkAgent>();
        services.AddScoped<IAgent, SecurityFrameworkAgent>();
        services.AddScoped<IAgentRegistry, AgentRegistry>();
        services.AddScoped<IAgentFrameworkService, AgentFrameworkService>();
        return services;
    }

    public static WebApplication MapAgentFramework(
        this WebApplication app)
    {
        app.MapGet("/api/agents/status", (
            IAgentFrameworkService framework) =>
            Results.Ok(framework.GetStatus()));

        app.MapGet("/api/agents", (
            IAgentFrameworkService framework) =>
            Results.Ok(framework.GetCatalog()));

        app.MapGet("/api/planner/status", () =>
            Results.Ok(PlannerAgentLimits.GetStatus()));

        app.MapGet("/api/research/status", () =>
            Results.Ok(ResearchAgentLimits.GetStatus()));

        app.MapGet("/api/developer-agent/status", () =>
            Results.Ok(DeveloperAgentLimits.GetStatus()));

        app.MapGet("/api/office-agent/status", () =>
            Results.Ok(OfficeAgentLimits.GetStatus()));

        app.MapGet("/api/operator-agent/status", () =>
            Results.Ok(OperatorAgentLimits.GetStatus()));

        app.MapGet("/api/reviewer-agent/status", () =>
            Results.Ok(ReviewerAgentLimits.GetStatus()));

        app.MapGet("/api/security-agent/status", () =>
            Results.Ok(SecurityAgentLimits.GetStatus()));

        app.MapGet("/api/agents/{agentId}", (
            string agentId,
            IAgentFrameworkService framework) =>
        {
            var agent = framework.GetAgent(agentId);
            return agent is null
                ? Results.NotFound(
                    new ApiError(
                        "Không tìm thấy agent đã đăng ký."))
                : Results.Ok(agent);
        });

        app.MapPost("/api/agents/{agentId}/execute", async (
            string agentId,
            AgentExecutionRequest request,
            IAgentFrameworkService framework,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(
                    await framework.ExecuteAsync(
                        agentId,
                        request,
                        cancellationToken));
            }
            catch (AgentValidationException exception)
            {
                return Results.BadRequest(
                    new ApiError(exception.Message));
            }
            catch (ReviewerOutputException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (OperatorOutputException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (OfficeOutputException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (DeveloperOutputException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (ResearchOutputException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (PlannerOutputException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(
                    new ApiError(exception.Message));
            }
            catch (AgentBusyException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (HttpRequestException exception)
            {
                var statusCode =
                    exception.StatusCode == HttpStatusCode.TooManyRequests
                        ? StatusCodes.Status429TooManyRequests
                        : StatusCodes.Status502BadGateway;
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: statusCode);
            }
            catch (TaskCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                return Results.Json(
                    new ApiError(
                        "Agent mất quá nhiều thời gian để phản hồi. Hãy thử lại."),
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
        });

        return app;
    }
}
