using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class KnowledgeQualityEndpoints
{
    public static IServiceCollection AddKnowledgeQuality(this IServiceCollection services)
    {
        services.AddSingleton<IKnowledgeQualityService, KnowledgeQualityService>();
        return services;
    }

    public static IEndpointRouteBuilder MapKnowledgeQuality(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/knowledge/quality/status", async (
            IKnowledgeQualityService qualityService,
            CancellationToken cancellationToken) =>
            Results.Ok(await qualityService.GetStatusAsync(cancellationToken)));

        endpoints.MapPost("/api/knowledge/quality/evaluate", async (
            KnowledgeEvaluationRequest request,
            IKnowledgeQualityService qualityService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await qualityService.EvaluateAsync(request, cancellationToken));
            }
            catch (KnowledgeDocumentValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapPost("/api/knowledge/quality/repair", async (
            IKnowledgeQualityService qualityService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await qualityService.RepairAsync(cancellationToken));
            }
            catch (KnowledgeDocumentValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        return endpoints;
    }
}
