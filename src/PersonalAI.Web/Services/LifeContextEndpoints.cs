using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class LifeContextEndpoints
{
    public static IServiceCollection AddLifeContext(
        this IServiceCollection services)
    {
        services.AddSingleton<ILifeContextService, LifeContextService>();
        return services;
    }

    public static WebApplication MapLifeContext(
        this WebApplication app)
    {
        app.MapGet(
            "/api/life-context/status",
            (ILifeContextService lifeContext) =>
                Results.Ok(
                    lifeContext.GetStatus()));

        app.MapGet(
            "/api/life-context/sources",
            async (
                ILifeContextService lifeContext,
                CancellationToken cancellationToken) =>
                Results.Ok(
                    await lifeContext.GetSourcesAsync(
                        cancellationToken)));

        app.MapPost(
            "/api/life-context/sources",
            async (
                CreateLifeContextSourceRequest request,
                ILifeContextService lifeContext,
                IAuditRecorder audit,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var source =
                        await lifeContext.CreateSourceAsync(
                            request,
                            cancellationToken);

                    audit.Record(
                        AuditAgents.User,
                        "life-context.source.create",
                        $"life-source:{source.Id:D}",
                        "user-confirmed-consent",
                        AuditResults.Succeeded,
                        workspaceId: source.WorkspaceId);

                    return Results.Created(
                        $"/api/life-context/sources/{source.Id:D}",
                        source);
                }
                catch (LifeContextConfirmationRequiredException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }
                catch (LifeContextConsentRequiredException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
            });

        app.MapPatch(
            "/api/life-context/sources/{sourceId:guid}/enabled",
            async (
                Guid sourceId,
                SetLifeContextSourceEnabledRequest request,
                ILifeContextService lifeContext,
                IAuditRecorder audit,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var source =
                        await lifeContext.SetSourceEnabledAsync(
                            sourceId,
                            request,
                            cancellationToken);

                    if (source is null)
                    {
                        return Results.NotFound();
                    }

                    audit.Record(
                        AuditAgents.User,
                        source.Enabled
                            ? "life-context.source.enable"
                            : "life-context.source.disable",
                        $"life-source:{source.Id:D}",
                        "user-confirmed-source-state",
                        AuditResults.Succeeded,
                        workspaceId: source.WorkspaceId);

                    return Results.Ok(source);
                }
                catch (LifeContextConfirmationRequiredException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }
                catch (LifeContextConsentRequiredException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }
            });

        app.MapPatch(
            "/api/life-context/sources/{sourceId:guid}/consent",
            async (
                Guid sourceId,
                SetLifeContextConsentRequest request,
                ILifeContextService lifeContext,
                IAuditRecorder audit,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var source =
                        await lifeContext.SetConsentAsync(
                            sourceId,
                            request,
                            cancellationToken);

                    if (source is null)
                    {
                        return Results.NotFound();
                    }

                    audit.Record(
                        AuditAgents.User,
                        request.Granted
                            ? "life-context.consent.grant"
                            : "life-context.consent.revoke",
                        $"life-source:{source.Id:D}",
                        request.Granted
                            ? "user-confirmed-consent"
                            : request.PurgeExisting
                                ? "user-confirmed-revoke-and-purge"
                                : "user-confirmed-revoke",
                        AuditResults.Succeeded,
                        workspaceId: source.WorkspaceId);

                    return Results.Ok(source);
                }
                catch (LifeContextConfirmationRequiredException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }
            });

        app.MapPost(
            "/api/life-context/sources/{sourceId:guid}/entries",
            async (
                Guid sourceId,
                CreateLifeContextEntryRequest request,
                ILifeContextService lifeContext,
                IAuditRecorder audit,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var entry =
                        await lifeContext.AddEntryAsync(
                            sourceId,
                            request,
                            cancellationToken);

                    audit.Record(
                        AuditAgents.User,
                        "life-context.entry.create",
                        $"life-entry:{entry.Id:D}",
                        "user-confirmed-snapshot",
                        AuditResults.Succeeded,
                        workspaceId: entry.WorkspaceId);

                    return Results.Created(
                        $"/api/life-context/entries/{entry.Id:D}",
                        entry);
                }
                catch (LifeContextConfirmationRequiredException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }
                catch (LifeContextConsentRequiredException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(
                        new ApiError(exception.Message));
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
            });

        app.MapGet(
            "/api/life-context/entries",
            async (
                Guid? sourceId,
                int? limit,
                ILifeContextService lifeContext,
                CancellationToken cancellationToken) =>
                Results.Ok(
                    await lifeContext.GetEntriesAsync(
                        sourceId,
                        limit ?? 100,
                        cancellationToken)));

        app.MapDelete(
            "/api/life-context/entries/{entryId:guid}",
            async (
                Guid entryId,
                ILifeContextService lifeContext,
                IAuditRecorder audit,
                IWorkspaceContextAccessor workspaceContext,
                CancellationToken cancellationToken) =>
            {
                var deleted =
                    await lifeContext.DeleteEntryAsync(
                        entryId,
                        cancellationToken);
                if (!deleted)
                {
                    return Results.NotFound();
                }

                audit.Record(
                    AuditAgents.User,
                    "life-context.entry.delete",
                    $"life-entry:{entryId:D}",
                    "user-request",
                    AuditResults.Succeeded,
                    workspaceId:
                        workspaceContext.CurrentWorkspaceId);

                return Results.NoContent();
            });

        app.MapDelete(
            "/api/life-context/sources/{sourceId:guid}",
            async (
                Guid sourceId,
                bool? confirmed,
                ILifeContextService lifeContext,
                IAuditRecorder audit,
                IWorkspaceContextAccessor workspaceContext,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var deleted =
                        await lifeContext.DeleteSourceAsync(
                            sourceId,
                            confirmed == true,
                            cancellationToken);
                    if (!deleted)
                    {
                        return Results.NotFound();
                    }

                    audit.Record(
                        AuditAgents.User,
                        "life-context.source.delete",
                        $"life-source:{sourceId:D}",
                        "user-confirmed-source-delete",
                        AuditResults.Succeeded,
                        workspaceId:
                            workspaceContext.CurrentWorkspaceId);

                    return Results.NoContent();
                }
                catch (LifeContextConfirmationRequiredException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }
            });

        return app;
    }
}
