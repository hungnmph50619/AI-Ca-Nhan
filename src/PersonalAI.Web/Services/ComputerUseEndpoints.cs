using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class ComputerUseEndpoints
{
    public static IServiceCollection AddComputerUse(
        this IServiceCollection services)
    {
        services.AddSingleton<IComputerUseService, WindowsComputerUseService>();
        services.AddSingleton<IPersonalAiTool, ComputerScreenInfoTool>();
        services.AddSingleton<IPersonalAiTool, ComputerCursorPositionTool>();
        services.AddSingleton<IPersonalAiTool, ComputerWindowsListTool>();
        services.AddSingleton<IPersonalAiTool, ComputerActiveWindowTool>();
        services.AddSingleton<IPersonalAiTool, ComputerFocusWindowTool>();
        services.AddSingleton<IPersonalAiTool, ComputerMoveCursorTool>();
        return services;
    }

    public static WebApplication MapComputerUse(
        this WebApplication app)
    {
        app.MapGet("/api/computer/status", (
            IComputerUseService computer) =>
            Results.Ok(computer.GetStatus()));

        return app;
    }
}
