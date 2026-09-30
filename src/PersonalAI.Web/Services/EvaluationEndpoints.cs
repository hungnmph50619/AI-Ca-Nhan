using PersonalAI.Web.Evaluation.Contracts;
using PersonalAI.Web.Evaluation.Core;
using PersonalAI.Web.Evaluation.Evaluators;
using PersonalAI.Web.Evaluation.Regression;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class EvaluationEndpoints
{
    public static IServiceCollection AddEvaluationFramework(this IServiceCollection services)
    {
        services.AddSingleton<IEvaluator, MinimapBoxEvaluator>();
        services.AddSingleton<IEvaluationEngine, EvaluationEngine>();
        services.AddSingleton<IRegressionDatasetStore, SqliteRegressionDatasetStore>();
        return services;
    }

    public static IEndpointRouteBuilder MapEvaluationFramework(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/evaluation/status", (IEvaluationEngine engine) =>
            Results.Ok(new
            {
                version = PersonalAiRelease.Version,
                frameworkVersion = "2.3.21",
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
