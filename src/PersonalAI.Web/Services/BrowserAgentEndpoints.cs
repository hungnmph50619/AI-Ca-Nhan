using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class BrowserAgentEndpoints
{
    public static IServiceCollection AddBrowserAgent(
        this IServiceCollection services)
    {
        services.AddSingleton<IBrowserAgentService, BrowserAgentService>();
        services.AddSingleton<IPersonalAiTool, BrowserSessionInfoTool>();
        services.AddSingleton<IPersonalAiTool, BrowserNavigateTool>();
        services.AddSingleton<IPersonalAiTool, BrowserPageObserveTool>();
        services.AddSingleton<IPersonalAiTool, BrowserLinkOpenTool>();
        return services;
    }

    public static WebApplication MapBrowserAgent(
        this WebApplication app)
    {
        app.MapGet("/api/browser/status", (
            IBrowserAgentService browser) =>
            Results.Ok(browser.GetStatus()));

        return app;
    }
}
