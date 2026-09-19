using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class DevelopmentEndpoints
{
    public static IServiceCollection AddDevelopmentAgent(
        this IServiceCollection services)
    {
        services.AddSingleton<IDevelopmentAgentService, DevelopmentAgentService>();
        services.AddSingleton<IPersonalAiTool, DevelopmentWorkspaceInspectTool>();
        services.AddSingleton<IPersonalAiTool, DevelopmentTextSearchTool>();
        services.AddSingleton<IPersonalAiTool, DevelopmentGitStatusTool>();
        services.AddSingleton<IPersonalAiTool, DevelopmentGitDiffTool>();
        services.AddSingleton<IPersonalAiTool, DevelopmentDotnetRestoreTool>();
        services.AddSingleton<IPersonalAiTool, DevelopmentDotnetBuildTool>();
        services.AddSingleton<IPersonalAiTool, DevelopmentDotnetTestTool>();
        return services;
    }

    public static WebApplication MapDevelopmentAgent(
        this WebApplication app)
    {
        app.MapGet("/api/development/status", (
            IDevelopmentAgentService development) =>
            Results.Ok(development.GetStatus()));

        return app;
    }
}
