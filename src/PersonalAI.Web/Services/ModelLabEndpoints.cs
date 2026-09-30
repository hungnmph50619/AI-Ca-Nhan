using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class ModelLabEndpoints
{
    public static IServiceCollection AddModelLab(
        this IServiceCollection services)
    {
        services.AddSingleton<IModelLabDatasetStore, SqliteModelLabDatasetStore>();
        services.AddScoped<IPersonalDatasetBuilder, PersonalDatasetBuilder>();
        services.AddScoped<IDataCleaningService, DataCleaningService>();
        services.AddSingleton<ITrainingJobStore, SqliteTrainingJobStore>();
        return services;
    }

    public static IEndpointRouteBuilder MapModelLab(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/model-lab/status", (
            IModelLabDatasetStore store) =>
            Results.Ok(store.GetStatus()));

        endpoints.MapGet("/api/model-lab/training/status", (
            ITrainingJobStore jobs) =>
            Results.Ok(jobs.GetStatus()));

        endpoints.MapGet("/api/model-lab/training/jobs", (
            ITrainingJobStore jobs) =>
            Results.Ok(jobs.GetAll()));

        endpoints.MapGet("/api/model-lab/training/jobs/{jobId:guid}", (
            Guid jobId,
            ITrainingJobStore jobs) =>
        {
            var job = jobs.Get(jobId);
            return job is null ? Results.NotFound() : Results.Ok(job);
        });

        endpoints.MapPost("/api/model-lab/training/jobs", (
            CreateTrainingJobRequest request,
            ITrainingJobStore jobs,
            IAuditRecorder audit) =>
        {
            try
            {
                var job = jobs.Create(request);
                audit.Record(
                    AuditAgents.User,
                    "model-lab.training-job.create",
                    $"model-lab-training-job:{job.Id:D}",
                    $"dataset:{job.DatasetId}:v{job.DatasetVersion};config:{job.ConfigSha256}",
                    AuditResults.Succeeded);
                return Results.Created(
                    $"/api/model-lab/training/jobs/{job.Id:D}",
                    job);
            }
            catch (TrainingJobValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (TrainingJobConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (ModelLabDatasetValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        endpoints.MapPost("/api/model-lab/training/jobs/{jobId:guid}/cancel", (
            Guid jobId,
            ITrainingJobStore jobs,
            IAuditRecorder audit) =>
        {
            try
            {
                var job = jobs.Cancel(jobId);
                if (job is null) return Results.NotFound();

                audit.Record(
                    AuditAgents.User,
                    "model-lab.training-job.cancel",
                    $"model-lab-training-job:{job.Id:D}",
                    "user-request",
                    AuditResults.Succeeded);
                return Results.Ok(job);
            }
            catch (TrainingJobConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
        });

        endpoints.MapGet("/api/model-lab/datasets", (
            IModelLabDatasetStore store) =>
            Results.Ok(store.GetAll()));

        endpoints.MapGet("/api/model-lab/datasets/{datasetId}", (
            string datasetId,
            IModelLabDatasetStore store) =>
        {
            try
            {
                var dataset = store.Get(datasetId);
                return dataset is null
                    ? Results.NotFound()
                    : Results.Ok(dataset);
            }
            catch (ModelLabDatasetValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapGet("/api/model-lab/datasets/{datasetId}/versions", (
            string datasetId,
            IModelLabDatasetStore store) =>
        {
            try
            {
                var dataset = store.Get(datasetId);
                if (dataset is null) return Results.NotFound();
                return Results.Ok(store.GetVersions(datasetId));
            }
            catch (ModelLabDatasetValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapGet("/api/model-lab/datasets/{datasetId}/versions/{version:int}", (
            string datasetId,
            int version,
            IModelLabDatasetStore store) =>
        {
            try
            {
                var snapshot = store.GetVersion(datasetId, version);
                return snapshot is null
                    ? Results.NotFound()
                    : Results.Ok(snapshot);
            }
            catch (ModelLabDatasetValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapPost("/api/model-lab/datasets", (
            CreateModelLabDatasetRequest request,
            IModelLabDatasetStore store,
            IAuditRecorder audit) =>
        {
            try
            {
                var dataset = store.Create(request);
                audit.Record(
                    AuditAgents.User,
                    "model-lab.dataset.create",
                    $"model-lab-dataset:{dataset.Id}",
                    "user-request",
                    AuditResults.Succeeded);
                return Results.Created(
                    $"/api/model-lab/datasets/{Uri.EscapeDataString(dataset.Id)}",
                    dataset);
            }
            catch (ModelLabDatasetValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ModelLabDatasetConflictException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
        });

        endpoints.MapPost("/api/model-lab/datasets/{datasetId}/versions", (
            string datasetId,
            CreateModelLabDatasetVersionRequest request,
            IModelLabDatasetStore store,
            IAuditRecorder audit) =>
        {
            try
            {
                var version = store.CreateVersion(datasetId, request);
                audit.Record(
                    AuditAgents.User,
                    "model-lab.dataset.version.create",
                    $"model-lab-dataset:{version.DatasetId}:v{version.Version}",
                    "user-request",
                    AuditResults.Succeeded);
                return Results.Created(
                    $"/api/model-lab/datasets/{Uri.EscapeDataString(version.DatasetId)}/versions/{version.Version}",
                    version);
            }
            catch (ModelLabDatasetValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ModelLabDatasetConflictException exception)
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

        endpoints.MapPost("/api/model-lab/personal-dataset/build", async (
            BuildPersonalDatasetRequest request,
            IPersonalDatasetBuilder builder,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await builder.BuildAsync(
                    request,
                    cancellationToken));
            }
            catch (PersonalDatasetBuilderValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ModelLabDatasetValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ModelLabDatasetConflictException exception)
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

        endpoints.MapPost("/api/model-lab/data-cleaning", (
            CleanModelLabDatasetRequest request,
            IDataCleaningService cleaner) =>
        {
            try
            {
                return Results.Ok(cleaner.Clean(request));
            }
            catch (DataCleaningValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ModelLabDatasetValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ModelLabDatasetConflictException exception)
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

        return endpoints;
    }
}
