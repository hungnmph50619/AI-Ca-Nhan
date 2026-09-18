using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class TaskEngineEndpoints
{
    public static IServiceCollection AddTaskEngine(this IServiceCollection services)
    {
        services.AddSingleton<IPersonalTaskStore, SqlitePersonalTaskStore>();
        services.AddScoped<ITaskEngineService, TaskEngineService>();
        return services;
    }

    public static WebApplication MapTaskEngine(this WebApplication app)
    {
        app.MapGet("/api/tasks", (ITaskEngineService taskEngine) =>
            Results.Ok(taskEngine.GetAll()));

        app.MapGet("/api/tasks/{taskId:guid}", (
            Guid taskId,
            ITaskEngineService taskEngine) =>
        {
            var task = taskEngine.Get(taskId);
            return task is null
                ? Results.NotFound(new ApiError("Không tìm thấy tác vụ."))
                : Results.Ok(task);
        });

        app.MapPost("/api/tasks/prepare", (
            PreparePersonalTaskRequest request,
            ITaskEngineService taskEngine) =>
        {
            try
            {
                return Results.Ok(taskEngine.Prepare(request));
            }
            catch (PersonalTaskValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/tasks", async (
            CreatePersonalTaskRequest request,
            ITaskEngineService taskEngine,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var task = await taskEngine.CreateAsync(
                    request.Goal,
                    cancellationToken);
                return Results.Created($"/api/tasks/{task.Id}", task);
            }
            catch (PersonalTaskValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (HttpRequestException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Results.Json(
                    new ApiError("Lập kế hoạch tác vụ mất quá nhiều thời gian. Hãy thử lại."),
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
        });

        app.MapPost("/api/tasks/{taskId:guid}/execute-next", async (
            Guid taskId,
            ExecutePersonalTaskStepRequest request,
            ITaskEngineService taskEngine,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await taskEngine.ExecuteNextAsync(
                    taskId,
                    request.Confirmed,
                    cancellationToken);

                if (result is null)
                {
                    return Results.NotFound(new ApiError("Không tìm thấy tác vụ."));
                }

                var statusCode = result.Execution.Status switch
                {
                    ToolExecutionStatuses.Succeeded => StatusCodes.Status200OK,
                    ToolExecutionStatuses.InvalidInput => StatusCodes.Status400BadRequest,
                    ToolExecutionStatuses.Denied => StatusCodes.Status403Forbidden,
                    ToolExecutionStatuses.NotFound => StatusCodes.Status404NotFound,
                    ToolExecutionStatuses.TimedOut => StatusCodes.Status504GatewayTimeout,
                    _ => StatusCodes.Status500InternalServerError
                };

                return Results.Json(result, statusCode: statusCode);
            }
            catch (PersonalTaskConfirmationRequiredException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (PersonalTaskValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/tasks/{taskId:guid}/retry-assessment", (
            Guid taskId,
            ITaskEngineService taskEngine) =>
        {
            var assessment = taskEngine.GetRetryAssessment(taskId);
            return assessment is null
                ? Results.NotFound(new ApiError("Không tìm thấy tác vụ."))
                : Results.Ok(assessment);
        });

        app.MapPost("/api/tasks/{taskId:guid}/retry", async (
            Guid taskId,
            RetryPersonalTaskStepRequest request,
            ITaskEngineService taskEngine,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await taskEngine.RetryFailedStepAsync(
                    taskId,
                    request.ConfirmedReview,
                    cancellationToken);
                return result is null
                    ? Results.NotFound(new ApiError("Không tìm thấy tác vụ."))
                    : Results.Ok(result);
            }
            catch (PersonalTaskConfirmationRequiredException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (PersonalTaskRetryBlockedException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (PersonalTaskValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/tasks/{taskId:guid}/resume", async (
            Guid taskId,
            ITaskEngineService taskEngine,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var task = await taskEngine.ResumeAsync(
                    taskId,
                    cancellationToken);
                return task is null
                    ? Results.NotFound(new ApiError("Không tìm thấy tác vụ."))
                    : Results.Ok(task);
            }
            catch (PersonalTaskValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/tasks/{taskId:guid}/cancel", async (
            Guid taskId,
            ITaskEngineService taskEngine,
            CancellationToken cancellationToken) =>
        {
            var task = await taskEngine.CancelAsync(
                taskId,
                cancellationToken);
            return task is null
                ? Results.NotFound(new ApiError("Không tìm thấy tác vụ."))
                : Results.Ok(task);
        });

        return app;
    }
}
