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

        app.MapGet("/api/system/logs/status", (
            IAuditStore auditStore,
            IWorkspaceContextAccessor workspaceContext) =>
        {
            _ = auditStore.GetSummary(
                workspaceContext.CurrentWorkspaceId);

            return Results.Ok(new SystemLogStatus(
                PersonalAiRelease.Version,
                workspaceContext.CurrentWorkspaceId,
                "local-sqlite",
                SqliteAuditStore.RetentionDays,
                SqliteAuditStore.MaximumEntries,
                SqliteAuditStore.MaximumQueryLimit,
                RequestCorrelationEnabled: true,
                RequestBodyLoggingEnabled: false,
                AuthorizationHeaderLoggingEnabled: false,
                QueryStringLoggingEnabled: false,
                BackwardCompatibleAuditApi: true,
                SystemLogLevels.All.Order(StringComparer.Ordinal).ToArray()));
        });

        app.MapGet("/api/system/logs", (
            int? limit,
            string? level,
            string? source,
            string? correlationId,
            string? agent,
            string? action,
            string? result,
            IAuditStore auditStore,
            IWorkspaceContextAccessor workspaceContext) =>
        {
            try
            {
                return Results.Ok(auditStore.GetRecent(
                    workspaceContext.CurrentWorkspaceId,
                    limit ?? 50,
                    agent,
                    action,
                    result,
                    level,
                    source,
                    correlationId));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/system/logs/summary", (
            IAuditStore auditStore,
            IWorkspaceContextAccessor workspaceContext) =>
            Results.Ok(auditStore.GetSummary(
                workspaceContext.CurrentWorkspaceId)));

        return app;
    }
}
