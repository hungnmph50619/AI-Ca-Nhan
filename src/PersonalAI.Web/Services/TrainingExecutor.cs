using System.Text.Json;
using PersonalAI.Web.ModelLab;

namespace PersonalAI.Web.Services;

public interface ITrainingExecutor
{
    TrainingExecutorStatus GetStatus();
    IReadOnlyList<TrainingExecutionRecord> GetAll();
    TrainingExecutionRecord? Get(Guid id);
    TrainingExecutionRecord Queue(Guid id,QueueTrainingJobRequest request);
    TrainingExecutionRecord Cancel(Guid id);
}

public sealed class TrainingExecutor(
    ITrainingExecutionStore executions,
    ITrainingJobStore jobs,
    ITrainingProviderRegistry providers,
    IModelArtifactStore artifacts,
    IModelRegistry modelRegistry,
    ICandidateTrainingPlanStore candidatePlans,
    IAuditRecorder audit) : BackgroundService, ITrainingExecutor
{
    private readonly SemaphoreSlim _signal=new(0);
    private readonly CancellationTokenSource _shutdown=new();

    public TrainingExecutorStatus GetStatus()=>executions.GetStatus();
    public IReadOnlyList<TrainingExecutionRecord> GetAll()=>executions.GetAll();
    public TrainingExecutionRecord? Get(Guid id)=>executions.Get(id);

    public TrainingExecutionRecord Queue(Guid id,QueueTrainingJobRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var job=jobs.Get(id)??throw new KeyNotFoundException("Không tìm thấy training job.");
        if(TrainingJobStatuses.IsTerminal(job.Status))
            throw new TrainingExecutionValidationException("Training job đã kết thúc.");
        _=providers.Get(request.ProviderId);
        var timeout=request.TimeoutSeconds??JsonTrainingExecutionStore.DefaultTimeoutSeconds;
        var execution=executions.Queue(id,request.ProviderId,timeout);
        audit.Record(AuditAgents.User,"model-lab.training-execution.queue",
            $"training-job:{id:D}",$"provider:{request.ProviderId}",AuditResults.Prepared);
        _signal.Release();
        return execution;
    }

    public TrainingExecutionRecord Cancel(Guid id)
    {
        var execution=executions.Get(id)??throw new KeyNotFoundException("Không tìm thấy training execution.");
        if(TrainingExecutionStatuses.IsTerminal(execution.Status)) return execution;
        var result=executions.Set(id,TrainingExecutionStatuses.Cancelled,reason:"user-request");
        audit.Record(AuditAgents.User,"model-lab.training-execution.cancel",
            $"training-job:{id:D}","user-request",AuditResults.Succeeded);
        return result;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        executions.RecoverInterrupted();
        while(!stoppingToken.IsCancellationRequested)
        {
            foreach (var pending in jobs.GetAll()
                .Where(x => x.Status == TrainingJobStatuses.Pending))
            {
                if (TrainingMethods.IsPeft(pending.TrainingMethod))
                    continue;

                if (executions.Get(pending.Id) is null)
                {
                    try
                    {
                        var providerId = pending.TrainingMethod.Equals(
                                LocalSmallModelTrainingProvider.Method,
                                StringComparison.OrdinalIgnoreCase)
                            ? LocalSmallModelTrainingProvider.ProviderId
                            : MockTrainingProvider.ProviderId;

                        executions.Queue(
                            pending.Id,
                            providerId,
                            JsonTrainingExecutionStore.DefaultTimeoutSeconds);
                    }
                    catch (TrainingExecutionValidationException)
                    {
                    }
                }
            }

            var next=executions.GetAll()
                .Where(x=>x.Status==TrainingExecutionStatuses.Queued)
                .OrderBy(x=>x.CreatedAt)
                .FirstOrDefault();

            if(next is null)
            {
                try{await _signal.WaitAsync(TimeSpan.FromSeconds(2),stoppingToken);}
                catch(OperationCanceledException){break;}
                continue;
            }

            await RunOneAsync(next,stoppingToken);
        }
    }

    private async Task RunOneAsync(TrainingExecutionRecord execution,CancellationToken stoppingToken)
    {
        try
        {
            var job=jobs.Get(execution.TrainingJobId)
                ??throw new KeyNotFoundException("Training job không còn tồn tại.");
            var provider=providers.Get(execution.ProviderId);
            var validation=provider.Validate(new TrainingProviderValidationRequest(
                provider.Id,job.BaseModel,job.TrainingMethod,job.Hyperparameters));
            if(!validation.Valid)
            {
                executions.Set(job.Id,TrainingExecutionStatuses.Failed,
                    reason:string.Join(" ",validation.Errors));
                return;
            }

            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(execution.TimeoutSeconds));
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken,timeout.Token);

            var handle=await provider.StartAsync(new TrainingProviderStartRequest(
                job.Id,
                job.DatasetId,
                job.DatasetVersion,
                job.DatasetSha256,
                job.BaseModel,
                job.TrainingMethod,
                job.Hyperparameters,
                job.Seed),linked.Token);
            executions.Set(job.Id,TrainingExecutionStatuses.Running,handle.ExternalJobId);

            while(!linked.IsCancellationRequested)
            {
                var current=executions.Get(job.Id);
                var jobState=jobs.Get(job.Id);
                if(current?.Status==TrainingExecutionStatuses.Cancelled ||
                   jobState?.Status==TrainingJobStatuses.Cancelled)
                {
                    await provider.CancelAsync(handle.ExternalJobId,CancellationToken.None);
                    return;
                }

                var progress=await provider.GetStatusAsync(handle.ExternalJobId,linked.Token);
                if(progress.Status==TrainingProviderExecutionStatuses.Completed)
                {
                    executions.Set(job.Id,TrainingExecutionStatuses.Completed,metrics:progress.Metrics);

                    if(progress.Metrics is JsonElement metrics &&
                       metrics.ValueKind==JsonValueKind.Object &&
                       metrics.TryGetProperty("artifactPath",out var artifactPathElement) &&
                       artifactPathElement.ValueKind==JsonValueKind.String &&
                       !string.IsNullOrWhiteSpace(artifactPathElement.GetString()))
                    {
                        var artifactPath=artifactPathElement.GetString()!;
                        var artifact = artifacts.Register(new RegisterModelArtifactRequest(
                            Name:$"{job.TrainingMethod}-{job.Id:D}",
                            Version:"1",
                            BaseModel:job.BaseModel,
                            TrainingJobId:job.Id,
                            DatasetId:job.DatasetId,
                            DatasetVersion:job.DatasetVersion,
                            DatasetSha256:job.DatasetSha256,
                            TrainingMethod:job.TrainingMethod,
                            ArtifactPath:artifactPath,
                            Metrics:metrics));

                        var candidatePlan = candidatePlans.GetByTrainingJob(job.Id);
                        if(candidatePlan is not null)
                        {
                            var model = modelRegistry.Register(
                                new RegisterModelVersionRequest(
                                    candidatePlan.Family,
                                    candidatePlan.Version,
                                    artifact.Id,
                                    candidatePlan.CompatibleTasks,
                                    candidatePlan.Runtime,
                                    $"candidate-plan:{candidatePlan.Id:D};verification:{candidatePlan.VerificationReportId:D}"));

                            candidatePlans.UpdateRegistration(
                                job.Id,
                                artifact.Id,
                                model.Id);

                            audit.Record(
                                AuditAgents.System,
                                "model-lab.candidate-training.register",
                                $"candidate-plan:{candidatePlan.Id:D}",
                                $"artifact:{artifact.Id:D};model-version:{model.Id:D};stage:{model.Stage}",
                                AuditResults.Succeeded);
                        }
                    }

                    return;
                }
                if(progress.Status==TrainingProviderExecutionStatuses.Failed)
                {
                    executions.Set(job.Id,TrainingExecutionStatuses.Failed,reason:progress.FailureReason);
                    return;
                }
                if(progress.Status==TrainingProviderExecutionStatuses.Cancelled)
                {
                    executions.Set(job.Id,TrainingExecutionStatuses.Cancelled);
                    return;
                }

                await Task.Delay(250,linked.Token);
            }
        }
        catch(OperationCanceledException) when(!stoppingToken.IsCancellationRequested)
        {
            executions.Set(execution.TrainingJobId,TrainingExecutionStatuses.Failed,reason:"Training timeout.");
        }
        catch(Exception ex)
        {
            executions.Set(execution.TrainingJobId,TrainingExecutionStatuses.Failed,reason:ex.Message);

            if(candidatePlans.GetByTrainingJob(execution.TrainingJobId) is not null)
            {
                try { candidatePlans.Fail(execution.TrainingJobId, ex.Message); }
                catch { }
            }
        }
    }
}
