using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class BrowserAgentEndpoints
{
    public static IServiceCollection AddBrowserAgent(
        this IServiceCollection services)
    {
        services.AddSingleton<IBrowserAgentService, BrowserAgentService>();
        services.AddSingleton<IPlaywrightBrowserAdapter, PlaywrightBrowserAdapter>();
        services.AddSingleton<IPlaywrightBrowserExecutionBackend, PlaywrightBrowserExecutionBackend>();
        services.AddSingleton<IHttpBrowserExecutionBackend, InternalHttpBrowserExecutionBackend>();
        services.AddSingleton<IBrowserExecutionBackend, CompositeBrowserExecutionBackend>();
        services.AddSingleton<IExecutionAgent, BrowserExecutionAgent>();
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

        app.MapGet("/api/browser/automation-status", (
            IPlaywrightBrowserAdapter playwright) =>
            Results.Ok(new
            {
                tenBoMay = "Playwright + Microsoft Edge",
                uuTienKhiCoThe = playwright.PreferredRuntimeAvailable,
                duPhong = "Browser Agent HTTP/HTML",
                javascript = playwright.PreferredRuntimeAvailable
                    ? "Có khi Playwright/Edge khả dụng"
                    : "Không ở adapter ưu tiên hiện tại",
                moTa = playwright.PreferredRuntimeAvailable
                    ? "Tác vụ web ưu tiên Playwright để đọc DOM sau JavaScript; nếu không xác minh được sẽ tự chuyển sang HTTP/HTML."
                    : "Playwright/Edge chưa ở runtime ưu tiên; hệ thống giữ Browser Agent HTTP/HTML làm đường dự phòng."
            }));

        return app;
    }
}
