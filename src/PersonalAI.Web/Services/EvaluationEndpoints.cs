using PersonalAI.Web.Evaluation.Contracts;
using PersonalAI.Web.Evaluation.Core;
using PersonalAI.Web.Evaluation.Evaluators;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class EvaluationEndpoints
{
    public static IServiceCollection AddEvaluationFramework(this IServiceCollection services)
    {
        services.AddSingleton<IEvaluator, MinimapBoxEvaluator>();
        services.AddSingleton<IEvaluationEngine, EvaluationEngine>();
        return services;
    }

    public static IEndpointRouteBuilder MapEvaluationFramework(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/evaluation/status", (IEvaluationEngine engine) =>
            Results.Ok(new
            {
                version = PersonalAiRelease.Version,
                frameworkVersion = "2.3.20",
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
