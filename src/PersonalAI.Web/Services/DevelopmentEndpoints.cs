using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class DevelopmentEndpoints
{
    public static IServiceCollection AddDevelopmentAgent(
        this IServiceCollection services)
    {
        services.AddSingleton<IDevelopmentAgentService, DevelopmentAgentService>();
        services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        services.AddSingleton<IDevelopmentWorktreeService, DevelopmentWorktreeService>();
        services.AddSingleton<IPersonalAiTool, DevelopmentWorkspaceInspectTool>();
        services.AddSingleton<IPersonalAiTool, DevelopmentTextSearchTool>();
        services.AddSingleton<IPersonalAiTool, DevelopmentGitStatusTool>();
        services.AddSingleton<IPersonalAiTool, DevelopmentGitDiffTool>();
        services.AddSingleton<IPersonalAiTool, DevelopmentDotnetRestoreTool>();
        services.AddSingleton<IPersonalAiTool, DevelopmentDotnetBuildTool>();
        services.AddSingleton<IPersonalAiTool, DevelopmentDotnetTestTool>();
        return services;
    }

    public static WebApplication MapDevelopmentAgent(
        this WebApplication app)
    {
        app.MapGet("/api/development/status", (
            IDevelopmentAgentService development) =>
            Results.Ok(development.GetStatus()));

        app.MapGet("/api/development/git/status", async (
            string? repositoryPath,
            ILocalGitRepositoryService git,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await git.StatusAsync(
                    repositoryPath ?? string.Empty,
                    cancellationToken));
            }
            catch (LocalGitRepositoryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/git/capabilities", (
            ILocalGitRepositoryService git) =>
            Results.Ok(git.GetStatus()));

        app.MapGet("/api/development/worktrees/status", (
            IDevelopmentWorktreeService worktrees) =>
            Results.Ok(worktrees.GetStatus()));

        app.MapGet("/api/development/worktrees", async (
            string? repositoryPath,
            IDevelopmentWorktreeService worktrees,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await worktrees.GetAllAsync(
                    repositoryPath ?? string.Empty,
                    cancellationToken));
            }
            catch (DevelopmentWorktreeValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/worktrees", async (
            CreateDevelopmentWorktreeRequest request,
            IDevelopmentWorktreeService worktrees,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await worktrees.CreateAsync(
                    request,
                    cancellationToken));
            }
            catch (DevelopmentWorktreeValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapDelete("/api/development/worktrees", async (
            RemoveDevelopmentWorktreeRequest request,
            IDevelopmentWorktreeService worktrees,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await worktrees.RemoveAsync(
                    request,
                    cancellationToken));
            }
            catch (DevelopmentWorktreeValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/git/branches", async (
            string? repositoryPath,
            ILocalGitRepositoryService git,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await git.BranchesAsync(
                    repositoryPath ?? string.Empty,
                    cancellationToken));
            }
            catch (LocalGitRepositoryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/git/fetch", async (
            LocalGitFetchRequest request,
            ILocalGitRepositoryService git,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await git.FetchAsync(request, cancellationToken));
            }
            catch (LocalGitRepositoryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/git/checkout", async (
            LocalGitCheckoutRequest request,
            ILocalGitRepositoryService git,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await git.CheckoutAsync(request, cancellationToken));
            }
            catch (LocalGitRepositoryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/git/branches", async (
            LocalGitCreateBranchRequest request,
            ILocalGitRepositoryService git,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await git.CreateBranchAsync(request, cancellationToken));
            }
            catch (LocalGitRepositoryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/git/diff", async (
            LocalGitDiffRequest request,
            ILocalGitRepositoryService git,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await git.DiffAsync(request, cancellationToken));
            }
            catch (LocalGitRepositoryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/git/log", async (
            LocalGitLogRequest request,
            ILocalGitRepositoryService git,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await git.LogAsync(request, cancellationToken));
            }
            catch (LocalGitRepositoryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/git/pull", async (
            LocalGitPullRequest request,
            ILocalGitRepositoryService git,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await git.PullAsync(request, cancellationToken));
            }
            catch (LocalGitRepositoryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/git/commit", async (
            LocalGitCommitRequest request,
            ILocalGitRepositoryService git,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await git.CommitAsync(request, cancellationToken));
            }
            catch (LocalGitRepositoryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/git/push", async (
            LocalGitPushRequest request,
            ILocalGitRepositoryService git,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await git.PushAsync(request, cancellationToken));
            }
            catch (LocalGitRepositoryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        return app;
    }
}
