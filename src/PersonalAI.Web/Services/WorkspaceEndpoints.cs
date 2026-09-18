using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class WorkspaceEndpoints
{
    public const string WorkspaceHeaderName = "X-PersonalAI-Workspace";

    public static IServiceCollection AddWorkspaceFoundation(
        this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IPersonalWorkspaceStore, PersonalWorkspaceStore>();
        services.AddSingleton<IWorkspaceContextAccessor, WorkspaceContextAccessor>();
        services.AddSingleton<IWorkspaceStoragePathResolver, WorkspaceStoragePathResolver>();
        return services;
    }

    public static WebApplication UseWorkspaceValidation(
        this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                await next();
                return;
            }

            var requested = context.Request.Headers[WorkspaceHeaderName]
                .FirstOrDefault()?
                .Trim();

            if (string.IsNullOrWhiteSpace(requested))
            {
                await next();
                return;
            }

            var store = context.RequestServices
                .GetRequiredService<IPersonalWorkspaceStore>();
            if (store.Exists(requested))
            {
                await next();
                return;
            }

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new ApiError(
                    "Không gian làm việc được yêu cầu không tồn tại."));
        });

        return app;
    }

    public static WebApplication MapWorkspaceFoundation(
        this WebApplication app)
    {
        app.MapGet("/api/workspaces", (
            IPersonalWorkspaceStore workspaceStore,
            IWorkspaceContextAccessor workspaceContext) =>
            Results.Ok(new WorkspaceListResponse(
                workspaceContext.CurrentWorkspaceId,
                PersonalWorkspaceStore.MaximumWorkspaces,
                workspaceStore.GetAll())));

        app.MapGet("/api/workspaces/{workspaceId}", (
            string workspaceId,
            IPersonalWorkspaceStore workspaceStore) =>
        {
            var workspace = workspaceStore.Get(workspaceId);
            return workspace is null
                ? Results.NotFound(new ApiError(
                    "Không tìm thấy không gian làm việc."))
                : Results.Ok(workspace);
        });

        app.MapPost("/api/workspaces", (
            CreatePersonalWorkspaceRequest request,
            IPersonalWorkspaceStore workspaceStore) =>
        {
            try
            {
                var workspace = workspaceStore.Create(request);
                return Results.Created(
                    $"/api/workspaces/{workspace.Id}",
                    workspace);
            }
            catch (WorkspaceValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPatch("/api/workspaces/{workspaceId}", (
            string workspaceId,
            UpdatePersonalWorkspaceRequest request,
            IPersonalWorkspaceStore workspaceStore) =>
        {
            try
            {
                var workspace = workspaceStore.Update(
                    workspaceId,
                    request);
                return workspace is null
                    ? Results.NotFound(new ApiError(
                        "Không tìm thấy không gian làm việc."))
                    : Results.Ok(workspace);
            }
            catch (WorkspaceValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        return app;
    }
}
