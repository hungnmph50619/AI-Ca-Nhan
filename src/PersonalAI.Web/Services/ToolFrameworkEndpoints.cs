using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class ToolFrameworkEndpoints
{
    public const string FrameworkVersion = "0.9.2";

    private static readonly string[] PermissionTypes =
    [
        ToolPermissions.Read,
        ToolPermissions.Write,
        ToolPermissions.Delete,
        ToolPermissions.External,
        ToolPermissions.Sensitive
    ];

    public static IServiceCollection AddToolFramework(this IServiceCollection services)
    {
        services.AddSingleton<IPersonalAiTool, TextStatsTool>();
        services.AddSingleton<IPersonalAiTool, LocalClockTool>();
        services.AddSingleton<IPersonalAiTool, MemorySearchTool>();
        services.AddSingleton<IPersonalAiTool, DocumentSearchTool>();
        services.AddSingleton<IPersonalAiTool, CalculatorTool>();
        services.AddSingleton<IPersonalAiTool, DateMathTool>();
        services.AddSingleton<IPersonalAiTool, AppSummaryTool>();
        services.AddSingleton<IWorkspaceFileService, WorkspaceFileService>();
        services.AddSingleton<IPersonalAiTool, WorkspaceListTool>();
        services.AddSingleton<IPersonalAiTool, WorkspaceReadTextTool>();
        services.AddSingleton<IPersonalAiTool, WorkspaceWriteTextTool>();
        services.AddSingleton<IPersonalAiTool, WorkspaceCreateDirectoryTool>();
        services.AddSingleton<IPersonalAiTool, WorkspaceMoveTool>();
        services.AddSingleton<IPersonalAiTool, WorkspaceDeleteTool>();
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<IToolInputValidator, ToolInputValidator>();
        services.AddSingleton<IToolPolicy, ToolPolicy>();
        services.AddSingleton<IToolExecutionService, ToolExecutionService>();
        services.AddSingleton<IToolActivityStore, SqliteToolActivityStore>();
        services.AddScoped<IToolResultSynthesisService, ToolResultSynthesisService>();
        services.AddScoped<IToolOrchestrationService, ToolOrchestrationService>();
        return services;
    }

    public static WebApplication MapToolFramework(this WebApplication app)
    {
        app.MapGet("/api/tools", (IToolRegistry registry) =>
            Results.Ok(new ToolCatalogResponse(
                FrameworkVersion,
                PermissionTypes,
                registry.GetAll())));

        app.MapGet("/api/tools/{toolName}", (string toolName, IToolRegistry registry) =>
        {
            return registry.TryGet(toolName, out var tool) && tool is not null
                ? Results.Ok(tool.Definition)
                : Results.NotFound(new ApiError("Không tìm thấy công cụ đã đăng ký."));
        });

        app.MapGet("/api/tools/activity", (int? limit, IToolActivityStore activityStore) =>
            Results.Ok(activityStore.GetRecent(limit ?? 30)));

        app.MapPost("/api/tools/execute", async (
            ToolExecutionRequest request,
            IToolExecutionService executor,
            IToolActivityStore activityStore,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var response = await executor.ExecuteAsync(request, cancellationToken);
            TryRecordDirectExecution(activityStore, loggerFactory, request, response);
            var statusCode = response.Status switch
            {
                ToolExecutionStatuses.Succeeded => StatusCodes.Status200OK,
                ToolExecutionStatuses.InvalidInput => StatusCodes.Status400BadRequest,
                ToolExecutionStatuses.Denied => StatusCodes.Status403Forbidden,
                ToolExecutionStatuses.NotFound => StatusCodes.Status404NotFound,
                ToolExecutionStatuses.TimedOut => StatusCodes.Status504GatewayTimeout,
                _ => StatusCodes.Status500InternalServerError
            };

            return Results.Json(response, statusCode: statusCode);
        });

        app.MapPost("/api/tools/orchestrate/prepare", (
            ToolProposalDraft request,
            IToolOrchestrationService orchestration) =>
        {
            try
            {
                return Results.Ok(orchestration.Prepare(request));
            }
            catch (ToolProposalValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/tools/orchestrate/execute", async (
            ExecuteToolProposalRequest request,
            IToolOrchestrationService orchestration,
            CancellationToken cancellationToken) =>
        {
            var response = await orchestration.ExecuteAsync(
                request.ProposalId,
                request.Confirmed,
                cancellationToken);
            if (response is null)
            {
                return Results.NotFound(new ApiError(
                    "Đề xuất công cụ không tồn tại, đã hết hạn hoặc đã được dùng."));
            }

            var statusCode = response.Execution.Status switch
            {
                ToolExecutionStatuses.Succeeded => StatusCodes.Status200OK,
                ToolExecutionStatuses.InvalidInput => StatusCodes.Status400BadRequest,
                ToolExecutionStatuses.Denied => StatusCodes.Status403Forbidden,
                ToolExecutionStatuses.NotFound => StatusCodes.Status404NotFound,
                ToolExecutionStatuses.TimedOut => StatusCodes.Status504GatewayTimeout,
                _ => StatusCodes.Status500InternalServerError
            };

            return Results.Json(response, statusCode: statusCode);
        });

        app.MapPost("/api/tools/orchestrate/synthesize", async (
            ToolResultSynthesisRequest request,
            IToolOrchestrationService orchestration,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var response = await orchestration.SynthesizeAsync(
                    request.InvocationId,
                    request.ConfirmedExternal,
                    cancellationToken);
                return response is null
                    ? Results.NotFound(new ApiError(
                        "Kết quả công cụ không tồn tại hoặc đã hết thời gian diễn giải."))
                    : Results.Ok(response);
            }
            catch (ToolExternalConfirmationRequiredException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ToolProposalValidationException exception)
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
                    new ApiError("Diễn giải kết quả bằng AI mất quá nhiều thời gian. Hãy thử lại."),
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
        });

        app.MapPost("/api/tools/orchestrate/continue-native", async (
            ToolNativeContinuationRequest request,
            IToolOrchestrationService orchestration,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var response = await orchestration.ContinueNativeAsync(
                    request.InvocationId,
                    request.ConfirmedExternal,
                    cancellationToken);
                return response is null
                    ? Results.NotFound(new ApiError(
                        "Kết quả công cụ không tồn tại hoặc lượt hoàn tất bằng AI đã hết hạn."))
                    : Results.Ok(response);
            }
            catch (ToolExternalConfirmationRequiredException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ToolProposalValidationException exception)
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
                    new ApiError("Hoàn tất câu trả lời bằng AI mất quá nhiều thời gian. Hãy thử lại."),
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
        });

        return app;
    }

    private static void TryRecordDirectExecution(
        IToolActivityStore activityStore,
        ILoggerFactory loggerFactory,
        ToolExecutionRequest request,
        ToolExecutionResponse response)
    {
        try
        {
            activityStore.Record(new ToolActivityEvent(
                ToolActivityEventTypes.DirectExecutionCompleted,
                response.ToolName,
                null,
                response.InvocationId,
                "direct",
                null,
                null,
                response.RequiredPermissions,
                request.Confirmed,
                null,
                response.Status,
                request.Arguments,
                response.Output,
                response.DurationMs));
        }
        catch (Exception exception)
        {
            loggerFactory
                .CreateLogger("ToolActivity")
                .LogWarning(
                    exception,
                    "Không thể ghi nhật ký lần chạy trực tiếp của công cụ {ToolName}.",
                    response.ToolName);
        }
    }
}
