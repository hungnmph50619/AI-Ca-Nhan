using PersonalAI.Web.Evaluation.Benchmarks;
using PersonalAI.Web.Evaluation.Comparison;
using PersonalAI.Web.Evaluation.Contracts;
using PersonalAI.Web.Evaluation.Core;
using PersonalAI.Web.Evaluation.Evaluators;
using PersonalAI.Web.Evaluation.Regression;
using PersonalAI.Web.Models;
using PersonalAI.Web.SelfImprovement;

namespace PersonalAI.Web.Services;

public static class EvaluationEndpoints
{
    public static IServiceCollection AddEvaluationFramework(this IServiceCollection services)
    {
        services.AddSingleton<IEvaluator, MinimapBoxEvaluator>();
        services.AddSingleton<IEvaluationEngine, EvaluationEngine>();
        services.AddSingleton<IRegressionDatasetStore, SqliteRegressionDatasetStore>();
        services.AddScoped<IAgentBenchmarkService, AgentBenchmarkService>();
        services.AddScoped<IModelComparisonService, ModelComparisonService>();
        services.AddScoped<ISelfEvaluationAgent, SelfEvaluationAgent>();
        services.AddScoped<IImprovementProposalService, ImprovementProposalService>();
        services.AddScoped<IAutomatedExperimentService, AutomatedExperimentService>();
        services.AddScoped<ISelfCodingService, SelfCodingService>();
        services.AddScoped<IAutomatedReviewService, AutomatedReviewService>();
        services.AddScoped<IAutoTestService, AutoTestService>();
        services.AddScoped<IAutoDeployService, AutoDeployService>();
        services.AddScoped<IAutomaticRollbackService, AutomaticRollbackService>();
        return services;
    }

    public static IEndpointRouteBuilder MapEvaluationFramework(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/evaluation/status", (IEvaluationEngine engine) =>
            Results.Ok(new
            {
                version = PersonalAiRelease.Version,
                frameworkVersion = "2.4.7",
                localOnly = true,
                maximumBatchSize = EvaluationEngine.MaximumBatchSize,
                maximumSummaryResults = EvaluationMetricsAggregator.MaximumResults,
                roadmapMetrics = EvaluationMetricCatalog.RoadmapMetrics,
                categories = engine.Categories
            }));

        endpoints.MapPost("/api/evaluation/run", (EvaluationCase evaluationCase, IEvaluationEngine engine) =>
        {
            try
            {
                return Results.Ok(engine.Evaluate(evaluationCase));
            }
            catch (EvaluationEngineException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapPost("/api/evaluation/run-batch", (IReadOnlyList<EvaluationCase> evaluationCases, IEvaluationEngine engine) =>
        {
            try
            {
                return Results.Ok(engine.EvaluateMany(evaluationCases));
            }
            catch (EvaluationEngineException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });


        endpoints.MapGet("/api/evaluation/regression/status", (IRegressionDatasetStore store) =>
            Results.Ok(store.GetStatus()));

        endpoints.MapGet("/api/evaluation/regression", (IRegressionDatasetStore store) =>
            Results.Ok(store.GetAll()));

        endpoints.MapGet("/api/evaluation/regression/export", (IRegressionDatasetStore store, IWorkspaceContextAccessor workspace) =>
            Results.Ok(new RegressionDatasetExport(
                1,
                PersonalAiRelease.Version,
                workspace.CurrentWorkspaceId,
                DateTimeOffset.UtcNow,
                store.GetAll())));

        endpoints.MapGet("/api/evaluation/regression/{caseId}", (string caseId, IRegressionDatasetStore store) =>
        {
            try
            {
                var item = store.Get(caseId);
                return item is null ? Results.NotFound() : Results.Ok(item);
            }
            catch (RegressionDatasetValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapPost("/api/evaluation/regression", (
            CreateRegressionDatasetItemRequest request,
            IRegressionDatasetStore store,
            IAuditRecorder audit) =>
        {
            try
            {
                var item = store.Create(request);
                audit.Record(
                    AuditAgents.User,
                    "evaluation.regression.create",
                    $"regression:{item.Id}",
                    "user-request",
                    AuditResults.Succeeded);
                return Results.Created($"/api/evaluation/regression/{Uri.EscapeDataString(item.Id)}", item);
            }
            catch (RegressionDatasetValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapPut("/api/evaluation/regression/{caseId}", (
            string caseId,
            UpdateRegressionDatasetItemRequest request,
            IRegressionDatasetStore store,
            IAuditRecorder audit) =>
        {
            try
            {
                var item = store.Update(caseId, request);
                if (item is null) return Results.NotFound();
                audit.Record(
                    AuditAgents.User,
                    "evaluation.regression.update",
                    $"regression:{item.Id}",
                    "user-request",
                    AuditResults.Succeeded);
                return Results.Ok(item);
            }
            catch (RegressionDatasetValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapDelete("/api/evaluation/regression/{caseId}", (
            string caseId,
            IRegressionDatasetStore store,
            IAuditRecorder audit) =>
        {
            try
            {
                if (!store.Delete(caseId)) return Results.NotFound();
                audit.Record(
                    AuditAgents.User,
                    "evaluation.regression.delete",
                    $"regression:{caseId}",
                    "user-request",
                    AuditResults.Succeeded);
                return Results.NoContent();
            }
            catch (RegressionDatasetValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapPost("/api/evaluation/benchmark/agents/run", async (
            RunAgentBenchmarkRequest request,
            IAgentBenchmarkService benchmark,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await benchmark.RunAsync(request, cancellationToken));
            }
            catch (AgentBenchmarkValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (AgentValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
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
        });

        endpoints.MapPost("/api/evaluation/model-comparison", (
            ModelComparisonRequest request,
            IModelComparisonService comparison) =>
        {
            try
            {
                return Results.Ok(comparison.Compare(request));
            }
            catch (ModelComparisonValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapPost("/api/self-improvement/evaluate", (
            SelfEvaluationRequest request,
            ISelfEvaluationAgent selfEvaluation) =>
        {
            try
            {
                return Results.Ok(selfEvaluation.Evaluate(request));
            }
            catch (SelfEvaluationValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapPost("/api/self-improvement/proposals", (
            ImprovementProposalRequest request,
            IImprovementProposalService proposals) =>
        {
            try
            {
                return Results.Ok(proposals.Create(request));
            }
            catch (ImprovementProposalValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapPost("/api/self-improvement/experiments/prepare", (
            PrepareAutomatedExperimentRequest request,
            IAutomatedExperimentService experiments) =>
        {
            try
            {
                return Results.Ok(experiments.Prepare(request));
            }
            catch (AutomatedExperimentValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapPost("/api/self-improvement/self-coding/run", async (
            RunSelfCodingRequest request,
            ISelfCodingService selfCoding,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await selfCoding.RunAsync(request, cancellationToken));
            }
            catch (SelfCodingValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ToolExecutionInputException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DevelopmentWorktreeValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
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

        endpoints.MapPost("/api/self-improvement/automated-review/run", async (
            RunAutomatedReviewRequest request,
            IAutomatedReviewService review,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await review.RunAsync(request, cancellationToken));
            }
            catch (AutomatedReviewValidationException exception)
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
            catch (ReviewerOutputException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });

        endpoints.MapPost("/api/self-improvement/auto-test/run", async (
            RunAutoTestRequest request,
            IAutoTestService autoTest,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await autoTest.RunAsync(request, cancellationToken));
            }
            catch (AutoTestValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (EvaluationEngineException exception)
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
        });

        endpoints.MapPost("/api/self-improvement/auto-deploy/run", async (
            RunAutoDeployRequest request,
            IAutoDeployService autoDeploy,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await autoDeploy.RunAsync(request, cancellationToken));
            }
            catch (AutoDeployValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ToolExecutionInputException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapPost("/api/self-improvement/auto-rollback/run", async (
            RunAutomaticRollbackRequest request,
            IAutomaticRollbackService rollback,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await rollback.RunAsync(request, cancellationToken));
            }
            catch (AutomaticRollbackValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ToolExecutionInputException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapPost("/api/evaluation/summarize", (IReadOnlyList<EvaluationResult> results) =>
        {
            try
            {
                return Results.Ok(EvaluationMetricsAggregator.Summarize(results));
            }
            catch (EvaluationEngineException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        return endpoints;
    }
}
