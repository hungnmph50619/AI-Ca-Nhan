using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class UndoEndpoints
{
    public static IServiceCollection AddUndoFoundation(
        this IServiceCollection services)
    {
        services.AddSingleton<IUndoStore, SqliteUndoStore>();
        services.AddSingleton<IUndoService, UndoService>();
        return services;
    }

    public static WebApplication MapUndoFoundation(
        this WebApplication app)
    {
        app.MapGet("/api/undo", (
            int? limit,
            IUndoService undo) =>
            Results.Ok(undo.GetRecent(limit ?? 50)));

        app.MapGet("/api/undo/{undoId:guid}/assessment", async (
            Guid undoId,
            IUndoService undo,
            CancellationToken cancellationToken) =>
        {
            var assessment = await undo.AssessAsync(
                undoId,
                cancellationToken);
            return assessment is null
                ? Results.NotFound(new ApiError(
                    "Không tìm thấy bản ghi hoàn tác trong workspace hiện tại."))
                : Results.Ok(assessment);
        });

        app.MapPost("/api/undo/{undoId:guid}/execute", async (
            Guid undoId,
            ExecuteUndoRequest request,
            IUndoService undo,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await undo.ExecuteAsync(
                    undoId,
                    request.Confirmed,
                    cancellationToken));
            }
            catch (UndoConfirmationRequiredException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (UndoConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(
                    new ApiError(exception.Message));
            }
        });

        return app;
    }
}
