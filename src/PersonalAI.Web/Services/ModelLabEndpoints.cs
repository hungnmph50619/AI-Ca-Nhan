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
        services.AddScoped<ITrainingDatasetValidationService, TrainingDatasetValidationService>();
        services.AddSingleton<ITrainingJobStore, SqliteTrainingJobStore>();
        services.AddSingleton<ITrainingProvider, MockTrainingProvider>();
        services.AddSingleton<ITrainingProvider, LocalSmallModelTrainingProvider>();
        services.AddSingleton<ITrainingProvider, PeftTrainingProvider>();
        services.AddSingleton<ITrainingProviderRegistry, TrainingProviderRegistry>();
        services.AddSingleton<ITrainingExecutionStore, JsonTrainingExecutionStore>();
        services.AddSingleton<IModelArtifactStore, JsonModelArtifactStore>();
        services.AddSingleton<IModelRegistry, JsonModelRegistry>();
        services.AddSingleton<ICandidateTrainingPlanStore, JsonCandidateTrainingPlanStore>();
        services.AddScoped<ICandidateTrainingService, CandidateTrainingService>();
        services.AddScoped<IRegisteredModelComparisonService, RegisteredModelComparisonService>();
        services.AddScoped<ISyntheticCriticService, SyntheticCriticService>();
        services.AddScoped<ISyntheticVerificationService, SyntheticVerificationService>();
        services.AddScoped<ISyntheticDataService, SyntheticDataService>();
        services.AddSingleton<TrainingExecutor>();
        services.AddSingleton<ITrainingExecutor>(sp => sp.GetRequiredService<TrainingExecutor>());
        services.AddHostedService(sp => sp.GetRequiredService<TrainingExecutor>());
        return services;
    }

    public static IEndpointRouteBuilder MapModelLab(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/model-lab/status", (
            IModelLabDatasetStore store) =>
            Results.Ok(store.GetStatus()));

        endpoints.MapGet("/api/model-lab/synthetic/verification/status", (
            ISyntheticVerificationService verification) =>
            Results.Ok(verification.GetStatus()));

        endpoints.MapGet("/api/model-lab/synthetic/verification/reports", (
            ISyntheticVerificationService verification) =>
            Results.Ok(verification.GetAll()));

        endpoints.MapGet("/api/model-lab/synthetic/verification/reports/{draftId:guid}/latest", (
            Guid draftId,
            ISyntheticVerificationService verification) =>
        {
            var report = verification.GetLatest(draftId);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        endpoints.MapPost("/api/model-lab/synthetic/verification/run", (
            VerifySyntheticDraftRequest request,
            ISyntheticDataService synthetic,
            ISyntheticVerificationService verification) =>
        {
            try
            {
                var draft = synthetic.Get(request.DraftId);
                if (draft is null)
                    return Results.NotFound(new ApiError("Không tìm thấy synthetic draft."));

                return Results.Ok(verification.Verify(draft, request));
            }
            catch (SyntheticVerificationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        endpoints.MapGet("/api/model-lab/synthetic/critic/status", (
            ISyntheticCriticService critic) =>
            Results.Ok(critic.GetStatus()));

        endpoints.MapGet("/api/model-lab/synthetic/critic/reports", (
            ISyntheticCriticService critic) =>
            Results.Ok(critic.GetAll()));

        endpoints.MapGet("/api/model-lab/synthetic/critic/reports/{draftId:guid}/latest", (
            Guid draftId,
            ISyntheticCriticService critic) =>
        {
            var report = critic.GetLatest(draftId);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        endpoints.MapPost("/api/model-lab/synthetic/critic/review", async (
            ReviewSyntheticDraftRequest request,
            ISyntheticDataService synthetic,
            ISyntheticCriticService critic,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var draft = synthetic.Get(request.DraftId);
                if (draft is null)
                    return Results.NotFound(new ApiError("Không tìm thấy synthetic draft."));

                return Results.Ok(await critic.ReviewAsync(
                    draft,
                    request,
                    cancellationToken));
            }
            catch (SyntheticCriticValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapGet("/api/model-lab/synthetic/status", (
            ISyntheticDataService synthetic) =>
            Results.Ok(synthetic.GetStatus()));

        endpoints.MapGet("/api/model-lab/synthetic/drafts", (
            ISyntheticDataService synthetic) =>
            Results.Ok(synthetic.GetAll()));

        endpoints.MapGet("/api/model-lab/synthetic/drafts/{draftId:guid}", (
            Guid draftId,
            ISyntheticDataService synthetic) =>
        {
            var draft = synthetic.Get(draftId);
            return draft is null ? Results.NotFound() : Results.Ok(draft);
        });

        endpoints.MapPost("/api/model-lab/synthetic/generate", async (
            GenerateSyntheticDataRequest request,
            ISyntheticDataService synthetic,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await synthetic.GenerateAsync(
                    request,
                    cancellationToken));
            }
            catch (SyntheticDataValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapPost("/api/model-lab/synthetic/commit", (
            CommitSyntheticDataRequest request,
            ISyntheticDataService synthetic) =>
        {
            try
            {
                return Results.Ok(synthetic.Commit(request));
            }
            catch (SyntheticDataValidationException exception)
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

        endpoints.MapGet("/api/model-lab/artifacts/status", (
            IModelArtifactStore artifacts) =>
            Results.Ok(artifacts.GetStatus()));

        endpoints.MapGet("/api/model-lab/artifacts", (
            IModelArtifactStore artifacts) =>
            Results.Ok(artifacts.GetAll()));

        endpoints.MapGet("/api/model-lab/artifacts/{artifactId:guid}", (
            Guid artifactId,
            IModelArtifactStore artifacts) =>
        {
            var artifact = artifacts.Get(artifactId);
            return artifact is null ? Results.NotFound() : Results.Ok(artifact);
        });

        endpoints.MapPost("/api/model-lab/artifacts/{artifactId:guid}/status", (
            Guid artifactId,
            UpdateModelArtifactStatusRequest request,
            IModelArtifactStore artifacts,
            IAuditRecorder audit) =>
        {
            try
            {
                var artifact = artifacts.UpdateStatus(artifactId, request);
                audit.Record(
                    AuditAgents.User,
                    "model-lab.artifact.status",
                    $"model-artifact:{artifact.Id:D}",
                    $"status:{artifact.Status}",
                    AuditResults.Succeeded);
                return Results.Ok(artifact);
            }
            catch (ModelArtifactValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        endpoints.MapGet("/api/model-lab/models/status", (
            IModelRegistry registry) =>
            Results.Ok(registry.GetStatus()));

        endpoints.MapGet("/api/model-lab/models/families", (
            IModelRegistry registry) =>
            Results.Ok(registry.GetFamilies()));

        endpoints.MapGet("/api/model-lab/models", (
            IModelRegistry registry) =>
            Results.Ok(registry.GetAll()));

        endpoints.MapGet("/api/model-lab/models/{modelId:guid}", (
            Guid modelId,
            IModelRegistry registry) =>
        {
            var model = registry.Get(modelId);
            return model is null ? Results.NotFound() : Results.Ok(model);
        });

        endpoints.MapGet("/api/model-lab/models/family/{family}", (
            string family,
            IModelRegistry registry) =>
        {
            try
            {
                return Results.Ok(registry.GetFamily(family));
            }
            catch (ModelRegistryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        endpoints.MapPost("/api/model-lab/models", (
            RegisterModelVersionRequest request,
            IModelRegistry registry,
            IAuditRecorder audit) =>
        {
            try
            {
                var model = registry.Register(request);
                audit.Record(
                    AuditAgents.User,
                    "model-lab.model-register",
                    $"model-version:{model.Id:D}",
                    $"family:{model.Family};version:{model.Version}",
                    AuditResults.Succeeded);
                return Results.Created(
                    $"/api/model-lab/models/{model.Id:D}",
                    model);
            }
            catch (ModelRegistryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ModelRegistryConflictException exception)
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

        endpoints.MapPost("/api/model-lab/models/{modelId:guid}/stage", (
            Guid modelId,
            UpdateModelDeploymentStageRequest request,
            IModelRegistry registry,
            IAuditRecorder audit) =>
        {
            try
            {
                var model = registry.UpdateStage(modelId, request);
                audit.Record(
                    AuditAgents.User,
                    "model-lab.model-stage",
                    $"model-version:{model.Id:D}",
                    $"stage:{model.Stage}",
                    AuditResults.Succeeded);
                return Results.Ok(model);
            }
            catch (ModelRegistryValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (ModelRegistryConflictException exception)
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

        endpoints.MapGet("/api/model-lab/model-comparisons/status", (
            IRegisteredModelComparisonService comparisons) =>
            Results.Ok(comparisons.GetStatus()));

        endpoints.MapGet("/api/model-lab/model-comparisons", (
            IRegisteredModelComparisonService comparisons) =>
            Results.Ok(comparisons.GetAll()));

        endpoints.MapGet("/api/model-lab/model-comparisons/{comparisonId:guid}", (
            Guid comparisonId,
            IRegisteredModelComparisonService comparisons) =>
        {
            var report = comparisons.Get(comparisonId);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        endpoints.MapPost("/api/model-lab/model-comparisons", (
            CompareRegisteredModelsRequest request,
            IRegisteredModelComparisonService comparisons) =>
        {
            try
            {
                return Results.Ok(comparisons.Compare(request));
            }
            catch (RegisteredModelComparisonValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        endpoints.MapGet("/api/model-lab/candidate-training/status", (
            ICandidateTrainingService candidates) =>
            Results.Ok(candidates.GetStatus()));

        endpoints.MapGet("/api/model-lab/candidate-training/plans", (
            ICandidateTrainingService candidates) =>
            Results.Ok(candidates.GetAll()));

        endpoints.MapGet("/api/model-lab/candidate-training/plans/{planId:guid}", (
            Guid planId,
            ICandidateTrainingService candidates) =>
        {
            var plan = candidates.Get(planId);
            return plan is null ? Results.NotFound() : Results.Ok(plan);
        });

        endpoints.MapPost("/api/model-lab/candidate-training/start", (
            StartCandidateTrainingRequest request,
            ICandidateTrainingService candidates) =>
        {
            try
            {
                return Results.Ok(candidates.Start(request));
            }
            catch (CandidateTrainingValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
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
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        endpoints.MapGet("/api/model-lab/training/providers", (
            ITrainingProviderRegistry registry) =>
            Results.Ok(registry.GetAll()));

        endpoints.MapPost("/api/model-lab/training/providers/{providerId}/validate", (
            string providerId,
            TrainingProviderValidationRequest request,
            ITrainingProviderRegistry registry) =>
        {
            try
            {
                var provider = registry.Get(providerId);
                var normalized = request with { ProviderId = provider.Id };
                return Results.Ok(provider.Validate(normalized));
            }
            catch (TrainingProviderValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new ApiError(exception.Message));
            }
        });

        endpoints.MapGet("/api/model-lab/training/status", (
            ITrainingJobStore jobs) =>
            Results.Ok(jobs.GetStatus()));

        endpoints.MapPost("/api/model-lab/training/validate", (
            ValidateTrainingDatasetRequest request,
            ITrainingDatasetValidationService validation) =>
        {
            try
            {
                return Results.Ok(validation.Validate(request));
            }
            catch (TrainingDatasetValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            catch (DataCleaningValidationException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
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
