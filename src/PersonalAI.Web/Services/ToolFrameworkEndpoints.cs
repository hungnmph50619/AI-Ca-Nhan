using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class ToolFrameworkEndpoints
{
    public const string FrameworkVersion = "0.8.5";

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

        app.MapPost("/api/tools/execute", async (
            ToolExecutionRequest request,
            IToolExecutionService executor,
            CancellationToken cancellationToken) =>
        {
            var response = await executor.ExecuteAsync(request, cancellationToken);
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

        return app;
    }
}
