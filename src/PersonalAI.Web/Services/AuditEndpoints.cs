using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class AuditEndpoints
{
    public static IServiceCollection AddAuditFoundation(
        this IServiceCollection services)
    {
        services.AddSingleton<IAuditStore, SqliteAuditStore>();
        services.AddSingleton<IAuditRecorder, AuditRecorder>();
        return services;
    }

    public static WebApplication MapAuditFoundation(
        this WebApplication app)
    {
        app.MapGet("/api/audit", (
            int? limit,
            string? agent,
            string? action,
            string? result,
            IAuditStore auditStore,
            IWorkspaceContextAccessor workspaceContext) =>
            Results.Ok(auditStore.GetRecent(
                workspaceContext.CurrentWorkspaceId,
                limit ?? 50,
                agent,
                action,
                result)));

        app.MapGet("/api/audit/summary", (
            IAuditStore auditStore,
            IWorkspaceContextAccessor workspaceContext) =>
            Results.Ok(auditStore.GetSummary(
                workspaceContext.CurrentWorkspaceId)));

        return app;
    }
}
