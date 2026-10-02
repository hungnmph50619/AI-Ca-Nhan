using PersonalAI.Web.Evaluation.Benchmarks;
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
        services.AddSingleton<IDevelopmentRunWorktreeService, DevelopmentRunWorktreeService>();
        services.AddScoped<IDevelopmentRecoveryService, DevelopmentRecoveryService>();
        services.AddScoped<IDependencyMaintenanceService, DependencyMaintenanceService>();
        services.AddScoped<INightlyImprovementService, NightlyImprovementService>();
        services.AddScoped<IAutonomousEmergencyStopService, AutonomousEmergencyStopService>();
        services.AddScoped<IAutonomousCodingService, AutonomousCodingService>();
        services.AddScoped<IAutonomousDevelopmentService, AutonomousDevelopmentService>();
        services.AddHostedService<NightlyImprovementBackgroundService>();
        services.AddSingleton<IDevelopmentLeaseService, DevelopmentLeaseService>();
        services.AddSingleton<IGitCredentialService, GitCredentialService>();
        services.AddSingleton<IDevelopmentEventBus, DevelopmentEventBus>();
        services.AddSingleton<IGitHubWebhookService, GitHubWebhookService>();
        services.AddSingleton<ICiMonitorService, CiMonitorService>();
        services.AddSingleton<IDevelopmentRunService, DevelopmentRunService>();
        services.AddSingleton<IImprovementBacklogService, ImprovementBacklogService>();
        services.AddSingleton<IRootCauseDiagnosisService, RootCauseDiagnosisService>();
        services.AddScoped<IDevelopmentAutoTestService, DevelopmentAutoTestService>();
        services.AddScoped<IDevelopmentReviewService, DevelopmentReviewService>();
        services.AddSingleton<IDevelopmentSecurityReportStore, DevelopmentSecurityReportStore>();
        services.AddScoped<IDevelopmentSecurityService, DevelopmentSecurityService>();
        services.AddSingleton<IDevelopmentBenchmarkReportStore, DevelopmentBenchmarkReportStore>();
        services.AddScoped<IDevelopmentBenchmarkService, DevelopmentBenchmarkService>();
        services.AddSingleton<IDevelopmentGitHubReportStore, DevelopmentGitHubReportStore>();
        services.AddScoped<IDevelopmentGitHubService, DevelopmentGitHubService>();
        services.AddSingleton<IDevelopmentMergePolicyReportStore, DevelopmentMergePolicyReportStore>();
        services.AddScoped<IDevelopmentMergePolicyService, DevelopmentMergePolicyService>();
        services.AddSingleton<IDevelopmentLocalSyncReportStore, DevelopmentLocalSyncReportStore>();
        services.AddScoped<IDevelopmentLocalSyncService, DevelopmentLocalSyncService>();
        services.AddHttpClient("development-github");
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

        app.MapGet("/api/development/autonomous/status", (
            IAutonomousDevelopmentService autonomous) =>
            Results.Ok(autonomous.GetStatus()));

        app.MapGet("/api/development/autonomous/emergency-stop", (
            IAutonomousEmergencyStopService emergency) =>
            Results.Ok(emergency.Get()));

        app.MapPost("/api/development/autonomous/emergency-stop", (
            SetAutonomousEmergencyStopRequest request,
            IAutonomousEmergencyStopService emergency) =>
        {
            try
            {
                return Results.Ok(emergency.Set(request));
            }
            catch (AutonomousDevelopmentValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/autonomous/run", async (
            RunAutonomousDevelopmentRequest request,
            IAutonomousDevelopmentService autonomous,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await autonomous.RunAsync(
                    request,
                    cancellationToken));
            }
            catch (AutonomousDevelopmentValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentRunValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentRunConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (DevelopmentRunWorktreeValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentWorktreeValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentTestValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentReviewValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentSecurityValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentBenchmarkValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentGitHubValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentMergePolicyValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentLocalSyncValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (GitCredentialValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ToolExecutionInputException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (AgentValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (AgentBusyException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/nightly/status", (
            INightlyImprovementService nightly) =>
            Results.Ok(nightly.GetStatus()));

        app.MapGet("/api/development/nightly/reports", (
            INightlyImprovementService nightly) =>
            Results.Ok(nightly.GetReports()));

        app.MapPost("/api/development/nightly/run", async (
            RunNightlyImprovementRequest request,
            INightlyImprovementService nightly,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await nightly.RunAsync(
                    request,
                    cancellationToken));
            }
            catch (NightlyImprovementValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentRunConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (DevelopmentRunValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ImprovementBacklogValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/dependencies/status", (
            IDependencyMaintenanceService dependencies) =>
            Results.Ok(dependencies.GetStatus()));

        app.MapGet("/api/development/dependencies/plans", (
            IDependencyMaintenanceService dependencies) =>
            Results.Ok(dependencies.GetAll()));

        app.MapGet("/api/development/dependencies/plans/{planId:guid}", (
            Guid planId,
            IDependencyMaintenanceService dependencies) =>
        {
            var plan = dependencies.Get(planId);
            return plan is null ? Results.NotFound() : Results.Ok(plan);
        });

        app.MapPost("/api/development/dependencies/plan", async (
            PlanDependencyMaintenanceRequest request,
            IDependencyMaintenanceService dependencies,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await dependencies.PlanAsync(
                    request,
                    cancellationToken));
            }
            catch (DependencyMaintenanceValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentRunWorktreeValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentWorktreeValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ToolExecutionInputException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/dependencies/apply", async (
            ApplyDependencyMaintenanceRequest request,
            IDependencyMaintenanceService dependencies,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await dependencies.ApplyAsync(
                    request,
                    cancellationToken));
            }
            catch (DependencyMaintenanceValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ToolExecutionInputException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/recovery/status", (
            IDevelopmentRecoveryService recovery) =>
            Results.Ok(recovery.GetStatus()));

        app.MapPost("/api/development/recovery/run", (
            RecoverDevelopmentRunRequest request,
            IDevelopmentRecoveryService recovery) =>
        {
            try
            {
                return Results.Ok(recovery.Recover(request));
            }
            catch (DevelopmentRecoveryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentRunConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (DevelopmentRunValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/run-worktrees/status", (
            IDevelopmentRunWorktreeService runWorktrees) =>
            Results.Ok(runWorktrees.GetStatus()));

        app.MapGet("/api/development/run-worktrees", (
            IDevelopmentRunWorktreeService runWorktrees) =>
            Results.Ok(runWorktrees.GetAll()));

        app.MapGet("/api/development/run-worktrees/{runId:guid}", (
            Guid runId,
            IDevelopmentRunWorktreeService runWorktrees) =>
        {
            var binding = runWorktrees.GetByRun(runId);
            return binding is null ? Results.NotFound() : Results.Ok(binding);
        });

        app.MapPost("/api/development/run-worktrees/ensure", async (
            EnsureDevelopmentRunWorktreeRequest request,
            IDevelopmentRunWorktreeService runWorktrees,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await runWorktrees.EnsureAsync(
                    request,
                    cancellationToken));
            }
            catch (DevelopmentRunWorktreeValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentWorktreeValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/run-worktrees/cleanup", async (
            CleanupDevelopmentRunWorktreeRequest request,
            IDevelopmentRunWorktreeService runWorktrees,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await runWorktrees.CleanupAsync(
                    request,
                    cancellationToken));
            }
            catch (DevelopmentRunWorktreeValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentWorktreeValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/local-sync/status", (
            IDevelopmentLocalSyncService sync) =>
            Results.Ok(sync.GetStatus()));

        app.MapGet("/api/development/local-sync/reports", (
            IDevelopmentLocalSyncService sync) =>
            Results.Ok(sync.GetAll()));

        app.MapGet("/api/development/local-sync/reports/{reportId:guid}", (
            Guid reportId,
            IDevelopmentLocalSyncService sync) =>
        {
            var report = sync.Get(reportId);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        app.MapPost("/api/development/local-sync/run", async (
            RunDevelopmentLocalSyncRequest request,
            IDevelopmentLocalSyncService sync,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await sync.RunAsync(
                    request,
                    cancellationToken));
            }
            catch (DevelopmentLocalSyncValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (LocalGitRepositoryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (GitCredentialValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/merge-policy/status", (
            IDevelopmentMergePolicyService policy) =>
            Results.Ok(policy.GetStatus()));

        app.MapGet("/api/development/merge-policy/reports", (
            IDevelopmentMergePolicyService policy) =>
            Results.Ok(policy.GetAll()));

        app.MapGet("/api/development/merge-policy/reports/{reportId:guid}", (
            Guid reportId,
            IDevelopmentMergePolicyService policy) =>
        {
            var report = policy.Get(reportId);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        app.MapPost("/api/development/merge-policy/evaluate", async (
            RunDevelopmentMergePolicyRequest request,
            IDevelopmentMergePolicyService policy,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await policy.EvaluateAndMergeAsync(
                    request,
                    cancellationToken));
            }
            catch (DevelopmentMergePolicyValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (GitCredentialValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/github/status", (
            IDevelopmentGitHubService github) =>
            Results.Ok(github.GetStatus()));

        app.MapGet("/api/development/github/reports", (
            IDevelopmentGitHubService github) =>
            Results.Ok(github.GetAll()));

        app.MapGet("/api/development/github/reports/{reportId:guid}", (
            Guid reportId,
            IDevelopmentGitHubService github) =>
        {
            var report = github.Get(reportId);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        app.MapPost("/api/development/github/run", async (
            RunDevelopmentGitHubRequest request,
            IDevelopmentGitHubService github,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await github.PushAndCreatePullRequestAsync(
                    request,
                    cancellationToken));
            }
            catch (DevelopmentGitHubValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (LocalGitRepositoryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentWorktreeValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (GitCredentialValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/github/reports/{reportId:guid}/ci/refresh", (
            Guid reportId,
            IDevelopmentGitHubService github) =>
        {
            try
            {
                return Results.Ok(github.RefreshCi(reportId));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/github/reports/{reportId:guid}/ci/repair", (
            Guid reportId,
            DevelopmentGitHubCiRepairRequest request,
            IDevelopmentGitHubService github) =>
        {
            try
            {
                return Results.Ok(github.RequestRepair(reportId, request));
            }
            catch (DevelopmentGitHubValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (CiRepairLimitException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (CiMonitorValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/benchmarks/status", (
            IDevelopmentBenchmarkService benchmarks) =>
            Results.Ok(benchmarks.GetStatus()));

        app.MapGet("/api/development/benchmarks/reports", (
            IDevelopmentBenchmarkService benchmarks) =>
            Results.Ok(benchmarks.GetAll()));

        app.MapGet("/api/development/benchmarks/reports/{reportId:guid}", (
            Guid reportId,
            IDevelopmentBenchmarkService benchmarks) =>
        {
            var report = benchmarks.Get(reportId);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        app.MapPost("/api/development/benchmarks/run", async (
            RunDevelopmentBenchmarkRequest request,
            IDevelopmentBenchmarkService benchmarks,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await benchmarks.RunAsync(
                    request,
                    cancellationToken));
            }
            catch (DevelopmentBenchmarkValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (AgentBenchmarkValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (AgentValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (AgentBusyException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/security/status", (
            IDevelopmentSecurityService security) =>
            Results.Ok(security.GetStatus()));

        app.MapGet("/api/development/security/reports", (
            IDevelopmentSecurityService security) =>
            Results.Ok(security.GetAll()));

        app.MapGet("/api/development/security/reports/{reportId:guid}", (
            Guid reportId,
            IDevelopmentSecurityService security) =>
        {
            var report = security.Get(reportId);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        app.MapPost("/api/development/security/run", async (
            RunDevelopmentSecurityRequest request,
            IDevelopmentSecurityService security,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await security.RunAsync(
                    request,
                    cancellationToken));
            }
            catch (DevelopmentSecurityValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (LocalGitRepositoryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ToolExecutionInputException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (AgentValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (AgentBusyException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/reviews/status", (
            IDevelopmentReviewService reviews) =>
            Results.Ok(reviews.GetStatus()));

        app.MapGet("/api/development/reviews", (
            IDevelopmentReviewService reviews) =>
            Results.Ok(reviews.GetAll()));

        app.MapGet("/api/development/reviews/{reportId:guid}", (
            Guid reportId,
            IDevelopmentReviewService reviews) =>
        {
            var report = reviews.Get(reportId);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        app.MapPost("/api/development/reviews/run", async (
            RunDevelopmentReviewRequest request,
            IDevelopmentReviewService reviews,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await reviews.RunAsync(
                    request,
                    cancellationToken));
            }
            catch (DevelopmentReviewValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (LocalGitRepositoryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (AgentValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (AgentBusyException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (ReviewerOutputException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/tests/status", (
            IDevelopmentAutoTestService tests) =>
            Results.Ok(tests.GetStatus()));

        app.MapGet("/api/development/tests/reports", (
            IDevelopmentAutoTestService tests) =>
            Results.Ok(tests.GetAll()));

        app.MapGet("/api/development/tests/reports/{reportId:guid}", (
            Guid reportId,
            IDevelopmentAutoTestService tests) =>
        {
            var report = tests.Get(reportId);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        app.MapPost("/api/development/tests/run", async (
            RunDevelopmentTestsRequest request,
            IDevelopmentAutoTestService tests,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await tests.RunAsync(
                    request,
                    cancellationToken));
            }
            catch (DevelopmentTestValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentWorktreeValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ToolExecutionInputException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/diagnoses/status", (
            IRootCauseDiagnosisService diagnoses) =>
            Results.Ok(diagnoses.GetStatus()));

        app.MapGet("/api/development/diagnoses", (
            IRootCauseDiagnosisService diagnoses) =>
            Results.Ok(diagnoses.GetAll()));

        app.MapGet("/api/development/diagnoses/{diagnosisId:guid}", (
            Guid diagnosisId,
            IRootCauseDiagnosisService diagnoses) =>
        {
            var diagnosis = diagnoses.Get(diagnosisId);
            return diagnosis is null ? Results.NotFound() : Results.Ok(diagnosis);
        });

        app.MapPost("/api/development/diagnoses", (
            StartRootCauseDiagnosisRequest request,
            IRootCauseDiagnosisService diagnoses) =>
        {
            try
            {
                return Results.Ok(diagnoses.Start(request));
            }
            catch (RootCauseDiagnosisConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (RootCauseDiagnosisValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/diagnoses/{diagnosisId:guid}/reproduction", (
            Guid diagnosisId,
            RecordReproductionRequest request,
            IRootCauseDiagnosisService diagnoses) =>
        {
            try
            {
                return Results.Ok(diagnoses.RecordReproduction(
                    diagnosisId,
                    request));
            }
            catch (RootCauseDiagnosisConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (RootCauseDiagnosisValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/diagnoses/{diagnosisId:guid}/hypotheses", (
            Guid diagnosisId,
            ProposeRootCauseHypothesisRequest request,
            IRootCauseDiagnosisService diagnoses) =>
        {
            try
            {
                return Results.Ok(diagnoses.ProposeHypothesis(
                    diagnosisId,
                    request));
            }
            catch (RootCauseDiagnosisConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (RootCauseDiagnosisValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/diagnoses/{diagnosisId:guid}/verify", (
            Guid diagnosisId,
            VerifyRootCauseHypothesisRequest request,
            IRootCauseDiagnosisService diagnoses) =>
        {
            try
            {
                return Results.Ok(diagnoses.VerifyHypothesis(
                    diagnosisId,
                    request));
            }
            catch (RootCauseDiagnosisConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (RootCauseDiagnosisValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/improvements/status", (
            IImprovementBacklogService backlog) =>
            Results.Ok(backlog.GetStatus()));

        app.MapGet("/api/development/improvements", (
            IImprovementBacklogService backlog) =>
            Results.Ok(backlog.GetAll()));

        app.MapGet("/api/development/improvements/{itemId:guid}", (
            Guid itemId,
            IImprovementBacklogService backlog) =>
        {
            var item = backlog.Get(itemId);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        app.MapPost("/api/development/improvements", (
            CreateImprovementBacklogRequest request,
            IImprovementBacklogService backlog) =>
        {
            try
            {
                return Results.Ok(backlog.Add(request));
            }
            catch (ImprovementBacklogValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/improvements/{itemId:guid}/state", (
            Guid itemId,
            UpdateImprovementBacklogStateRequest request,
            IImprovementBacklogService backlog) =>
        {
            try
            {
                return Results.Ok(backlog.UpdateState(itemId, request));
            }
            catch (ImprovementBacklogValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/improvements/import-ci", (
            IImprovementBacklogService backlog) =>
            Results.Ok(backlog.ImportFailedCiRuns()));

        app.MapGet("/api/development/runs/status", (
            IDevelopmentRunService runs) =>
            Results.Ok(runs.GetStatus()));

        app.MapGet("/api/development/runs", (
            IDevelopmentRunService runs) =>
            Results.Ok(runs.GetAll()));

        app.MapGet("/api/development/runs/{runId:guid}", (
            Guid runId,
            IDevelopmentRunService runs) =>
        {
            var run = runs.Get(runId);
            return run is null ? Results.NotFound() : Results.Ok(run);
        });

        app.MapPost("/api/development/runs", (
            CreateDevelopmentRunRequest request,
            IDevelopmentRunService runs) =>
        {
            try
            {
                return Results.Ok(runs.Create(request));
            }
            catch (DevelopmentRunConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (DevelopmentRunValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/runs/{runId:guid}/advance", (
            Guid runId,
            AdvanceDevelopmentRunRequest request,
            IDevelopmentRunService runs) =>
        {
            try
            {
                return Results.Ok(runs.Advance(runId, request));
            }
            catch (DevelopmentRunConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (DevelopmentRunValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/runs/{runId:guid}/fail", (
            Guid runId,
            FailDevelopmentRunRequest request,
            IDevelopmentRunService runs) =>
        {
            try
            {
                return Results.Ok(runs.Fail(runId, request));
            }
            catch (DevelopmentRunValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/runs/{runId:guid}/cancel", (
            Guid runId,
            CancelDevelopmentRunRequest request,
            IDevelopmentRunService runs) =>
        {
            try
            {
                return Results.Ok(runs.Cancel(runId, request));
            }
            catch (DevelopmentRunValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/runs/{runId:guid}/resume", (
            Guid runId,
            IDevelopmentRunService runs) =>
        {
            try
            {
                return Results.Ok(runs.Resume(runId));
            }
            catch (DevelopmentRunValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/ci/status", (
            ICiMonitorService ci) =>
            Results.Ok(ci.GetStatus()));

        app.MapGet("/api/development/ci/runs", (
            ICiMonitorService ci) =>
            Results.Ok(ci.GetAll()));

        app.MapGet("/api/development/ci/runs/{runId:guid}", (
            Guid runId,
            ICiMonitorService ci) =>
        {
            var run = ci.Get(runId);
            return run is null ? Results.NotFound() : Results.Ok(run);
        });

        app.MapPost("/api/development/ci/refresh", (
            ICiMonitorService ci) =>
            Results.Ok(ci.RefreshFromEvents()));

        app.MapPost("/api/development/ci/runs/{runId:guid}/failure-log", (
            Guid runId,
            RecordCiFailureLogRequest request,
            ICiMonitorService ci) =>
        {
            try
            {
                return Results.Ok(ci.RecordFailureLog(
                    request with { RunId = runId }));
            }
            catch (CiMonitorValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/ci/runs/{runId:guid}/repair", (
            Guid runId,
            RequestCiRepairRequest request,
            ICiMonitorService ci) =>
        {
            try
            {
                return Results.Ok(ci.RequestRepair(
                    request with { RunId = runId }));
            }
            catch (CiRepairLimitException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (CiMonitorValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/ci/runs/{runId:guid}/timeout", (
            Guid runId,
            MarkCiRunTimedOutRequest request,
            ICiMonitorService ci) =>
        {
            try
            {
                return Results.Ok(ci.MarkTimedOut(
                    request with { RunId = runId }));
            }
            catch (CiMonitorValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/ci/runs/{runId:guid}/cancel", (
            Guid runId,
            CancelCiRunRequest request,
            ICiMonitorService ci) =>
        {
            try
            {
                return Results.Ok(ci.Cancel(
                    request with { RunId = runId }));
            }
            catch (CiMonitorValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/github-webhook/status", (
            IGitHubWebhookService webhook) =>
            Results.Ok(webhook.GetStatus()));

        app.MapPost("/api/development/github-webhook/configure", (
            ConfigureGitHubWebhookRequest request,
            IGitHubWebhookService webhook) =>
        {
            try
            {
                webhook.Configure(request);
                return Results.Ok(webhook.GetStatus());
            }
            catch (GitHubWebhookValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/events", (
            int? maximum,
            IDevelopmentEventBus eventBus) =>
            Results.Ok(eventBus.GetRecent(maximum ?? 100)));

        app.MapPost("/api/development/github-webhook", async (
            HttpRequest request,
            IGitHubWebhookService webhook,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (request.ContentLength is > GitHubWebhookService.MaximumPayloadBytes)
                {
                    return Results.Json(
                        new ApiError("GitHub webhook payload vượt giới hạn."),
                        statusCode: StatusCodes.Status413PayloadTooLarge);
                }

                await using var memory = new MemoryStream();
                var buffer = new byte[81920];
                var total = 0;
                while (true)
                {
                    var read = await request.Body.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        cancellationToken);
                    if (read == 0) break;

                    total += read;
                    if (total > GitHubWebhookService.MaximumPayloadBytes)
                    {
                        return Results.Json(
                            new ApiError("GitHub webhook payload vượt giới hạn."),
                            statusCode: StatusCodes.Status413PayloadTooLarge);
                    }

                    await memory.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken);
                }

                var eventType = request.Headers["X-GitHub-Event"].ToString();
                var deliveryId = request.Headers["X-GitHub-Delivery"].ToString();
                var signature = request.Headers["X-Hub-Signature-256"].ToString();

                return Results.Ok(webhook.Receive(
                    eventType,
                    deliveryId,
                    signature,
                    memory.ToArray()));
            }
            catch (GitHubWebhookSignatureException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status401Unauthorized);
            }
            catch (GitHubWebhookValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/git-credentials/status", (
            IGitCredentialService credentials) =>
            Results.Ok(credentials.GetStatus()));

        app.MapGet("/api/development/git-credentials", (
            IGitCredentialService credentials) =>
            Results.Ok(credentials.GetAll()));

        app.MapPost("/api/development/git-credentials", (
            CreateGitCredentialRequest request,
            IGitCredentialService credentials) =>
        {
            try
            {
                return Results.Ok(credentials.Create(request));
            }
            catch (GitCredentialValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapDelete("/api/development/git-credentials", (
            [Microsoft.AspNetCore.Mvc.FromBody] DeleteGitCredentialRequest request,
            IGitCredentialService credentials) =>
        {
            try
            {
                credentials.Delete(request);
                return Results.NoContent();
            }
            catch (GitCredentialValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/development/leases/status", (
            IDevelopmentLeaseService leases) =>
            Results.Ok(leases.GetStatus()));

        app.MapGet("/api/development/leases", (
            IDevelopmentLeaseService leases) =>
            Results.Ok(leases.GetActive()));

        app.MapPost("/api/development/leases/acquire", (
            AcquireDevelopmentLeaseRequest request,
            IDevelopmentLeaseService leases) =>
        {
            try
            {
                return Results.Ok(leases.Acquire(request));
            }
            catch (DevelopmentLeaseValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentLeaseConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
        });

        app.MapPost("/api/development/leases/renew", (
            RenewDevelopmentLeaseRequest request,
            IDevelopmentLeaseService leases) =>
        {
            try
            {
                return Results.Ok(leases.Renew(request));
            }
            catch (DevelopmentLeaseValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentLeaseConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/development/leases/release", (
            ReleaseDevelopmentLeaseRequest request,
            IDevelopmentLeaseService leases) =>
        {
            try
            {
                leases.Release(request);
                return Results.NoContent();
            }
            catch (DevelopmentLeaseValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentLeaseConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

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
            [Microsoft.AspNetCore.Mvc.FromBody] RemoveDevelopmentWorktreeRequest request,
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
