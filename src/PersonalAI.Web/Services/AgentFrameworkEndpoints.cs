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
        services.AddScoped<IAgentOrchestrationService, AgentOrchestrationService>();
        services.AddSingleton<IAgentExecutionService, AgentExecutionService>();
        return services;
    }

    public static WebApplication MapAgentFramework(this WebApplication app)
    {
        app.MapGet("/api/agents/execution/tasks", (IAgentExecutionService execution) =>
            Results.Ok(execution.GetAll()));

        app.MapPost("/api/agents/execution/tasks", (
            CreateAgentExecutionRequest request,
            IAgentExecutionService execution) =>
        {
            try
            {
                return Results.Ok(execution.Create(request.Goal));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        return MapExistingAgentEndpoints(app);
    }

    private static WebApplication MapExistingAgentEndpoints(WebApplication app)
    {
        return app;
    }
}
