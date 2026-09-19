using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class AutomationEndpoints
{
    public static IServiceCollection AddAutomationFoundation(
        this IServiceCollection services)
    {
        services.AddSingleton<IAutomationStore, SqliteAutomationStore>();
        services.AddSingleton<IAutomationCoordinator, AutomationCoordinator>();
        services.AddScoped<IAutomationService, AutomationService>();
        services.AddHostedService<AutomationBackgroundService>();
        return services;
    }

    public static WebApplication MapAutomationFoundation(
        this WebApplication app)
    {
        app.MapGet("/api/automations/status", (
            IAutomationService automation) =>
            Results.Ok(automation.GetStatus()));

        app.MapGet("/api/automations", (
            IAutomationService automation) =>
            Results.Ok(automation.GetAll()));

        app.MapGet("/api/automations/{automationId:guid}", (
            Guid automationId,
            IAutomationService automation) =>
        {
            var item = automation.Get(automationId);
            return item is null
                ? Results.NotFound(
                    new ApiError(
                        "Không tìm thấy automation trong workspace hiện tại."))
                : Results.Ok(item);
        });

        app.MapPost("/api/automations", (
            CreateAutomationRequest request,
            IAutomationService automation) =>
        {
            try
            {
                var item = automation.Create(request);
                return Results.Created(
                    $"/api/automations/{item.Id:D}",
                    item);
            }
            catch (AutomationConfirmationRequiredException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode:
                        StatusCodes.Status403Forbidden);
            }
            catch (AutomationValidationException exception)
            {
                return Results.BadRequest(
                    new ApiError(exception.Message));
            }
        });

        app.MapPatch(
            "/api/automations/{automationId:guid}/enabled",
            (
                Guid automationId,
                SetAutomationEnabledRequest request,
                IAutomationService automation) =>
            {
                try
                {
                    var item =
                        automation.SetEnabled(
                            automationId,
                            request);
                    return item is null
                        ? Results.NotFound(
                            new ApiError(
                                "Không tìm thấy automation."))
                        : Results.Ok(item);
                }
                catch (AutomationConfirmationRequiredException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }
                catch (AutomationValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
            });

        app.MapPost(
            "/api/automations/{automationId:guid}/resume",
            (
                Guid automationId,
                AutomationConfirmedRequest request,
                IAutomationService automation) =>
            {
                try
                {
                    var item =
                        automation.Resume(
                            automationId,
                            request.Confirmed);
                    return item is null
                        ? Results.NotFound(
                            new ApiError(
                                "Không tìm thấy automation."))
                        : Results.Ok(item);
                }
                catch (AutomationConfirmationRequiredException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }
                catch (AutomationValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
            });

        app.MapPost(
            "/api/automations/{automationId:guid}/run-now",
            async (
                Guid automationId,
                AutomationConfirmedRequest request,
                IAutomationService automation,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var result =
                        await automation.RunNowAsync(
                            automationId,
                            request.Confirmed,
                            cancellationToken);
                    return result is null
                        ? Results.NotFound(
                            new ApiError(
                                "Không tìm thấy automation."))
                        : Results.Ok(result);
                }
                catch (AutomationConfirmationRequiredException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }
                catch (AutomationValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
            });

        app.MapDelete(
            "/api/automations/{automationId:guid}",
            (
                Guid automationId,
                bool? confirmed,
                IAutomationService automation) =>
            {
                try
                {
                    return automation.Delete(
                        automationId,
                        confirmed == true)
                        ? Results.NoContent()
                        : Results.NotFound(
                            new ApiError(
                                "Không tìm thấy automation."));
                }
                catch (AutomationConfirmationRequiredException exception)
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
