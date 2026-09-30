using System.Text.Json;
using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ITrainingExecutionStore
{
    TrainingExecutorStatus GetStatus();
    IReadOnlyList<TrainingExecutionRecord> GetAll();
    TrainingExecutionRecord? Get(Guid id);
    TrainingExecutionRecord Queue(Guid id,string providerId,int timeoutSeconds);
    TrainingExecutionRecord Set(Guid id,string status,string? externalJobId=null,string? reason=null,JsonElement? metrics=null);
    int RecoverInterrupted();
}

public sealed class JsonTrainingExecutionStore(
    IConfiguration configuration,
    IWorkspaceContextAccessor workspace) : ITrainingExecutionStore
{
    public const int DefaultTimeoutSeconds=300;
    public const int MaximumTimeoutSeconds=3600;
    private readonly object _gate=new();
    private readonly string _root=ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web){WriteIndented=true};

    public TrainingExecutorStatus GetStatus()
    {
        var all=GetAll();
        return new(PersonalAiRelease.Version,
            all.Count(x=>x.Status==TrainingExecutionStatuses.Queued),
            all.Count(x=>x.Status==TrainingExecutionStatuses.Running),
            all.Count(x=>x.Status==TrainingExecutionStatuses.Failed),
            all.Count(x=>x.Status==TrainingExecutionStatuses.Completed),
            all.Count(x=>x.Status==TrainingExecutionStatuses.Cancelled),
            1,DefaultTimeoutSeconds,true,true);
    }

    public IReadOnlyList<TrainingExecutionRecord> GetAll()
    {
        lock(_gate) return Load().OrderByDescending(x=>x.CreatedAt).ToArray();
    }

    public TrainingExecutionRecord? Get(Guid id) =>
        GetAll().FirstOrDefault(x=>x.TrainingJobId==id);

    public TrainingExecutionRecord Queue(Guid id,string providerId,int timeoutSeconds)
    {
        if(timeoutSeconds is <1 or >MaximumTimeoutSeconds)
            throw new TrainingExecutionValidationException("Timeout không hợp lệ.");
        var provider=(providerId??"").Trim();
        if(provider.Length is <1 or >80)
            throw new TrainingExecutionValidationException("ProviderId không hợp lệ.");

        lock(_gate)
        {
            var list=Load();
            var old=list.FirstOrDefault(x=>x.TrainingJobId==id);
            if(old is not null && !TrainingExecutionStatuses.IsTerminal(old.Status) &&
               old.Status!=TrainingExecutionStatuses.Interrupted)
                throw new TrainingExecutionValidationException("Job đang có execution hoạt động.");

            list.RemoveAll(x=>x.TrainingJobId==id);
            var now=DateTimeOffset.UtcNow;
            var item=new TrainingExecutionRecord(id,workspace.CurrentWorkspaceId,provider,null,
                TrainingExecutionStatuses.Queued,(old?.Attempt??0)+1,timeoutSeconds,
                old?.CreatedAt??now,now,null,null,null,null);
            list.Add(item);
            Save(list);
            return item;
        }
    }

    public TrainingExecutionRecord Set(
        Guid id,string status,string? externalJobId=null,string? reason=null,JsonElement? metrics=null)
    {
        lock(_gate)
        {
            var list=Load();
            var index=list.FindIndex(x=>x.TrainingJobId==id);
            if(index<0) throw new KeyNotFoundException("Không tìm thấy training execution.");
            var old=list[index];
            var now=DateTimeOffset.UtcNow;
            var item=old with{
                Status=status,
                ExternalJobId=externalJobId??old.ExternalJobId,
                FailureReason=reason,
                Metrics=metrics??old.Metrics,
                UpdatedAt=now,
                StartedAt=status==TrainingExecutionStatuses.Running ? old.StartedAt??now : old.StartedAt,
                CompletedAt=TrainingExecutionStatuses.IsTerminal(status)?now:old.CompletedAt
            };
            list[index]=item; Save(list); return item;
        }
    }

    public int RecoverInterrupted()
    {
        lock(_gate)
        {
            var list=Load(); var count=0; var now=DateTimeOffset.UtcNow;
            for(var i=0;i<list.Count;i++)
            {
                if(list[i].Status!=TrainingExecutionStatuses.Running) continue;
                list[i]=list[i] with{
                    Status=TrainingExecutionStatuses.Interrupted,
                    FailureReason="Ứng dụng đã dừng khi job đang chạy.",
                    UpdatedAt=now
                };
                count++;
            }
            if(count>0) Save(list);
            return count;
        }
    }

    private List<TrainingExecutionRecord> Load()
    {
        var path=PathForWorkspace();
        if(!File.Exists(path)) return [];
        try{return JsonSerializer.Deserialize<List<TrainingExecutionRecord>>(File.ReadAllText(path),Options)??[];}
        catch(JsonException){return [];}
    }

    private void Save(List<TrainingExecutionRecord> items)
    {
        Directory.CreateDirectory(_root);
        var path=PathForWorkspace(); var temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(items,Options));
        File.Move(temp,path,true);
    }

    private string PathForWorkspace()
    {
        var safe=string.Concat(workspace.CurrentWorkspaceId.Select(c=>char.IsLetterOrDigit(c)||c is '-' or '_'?c:'_'));
        return Path.Combine(_root,$"training-executions-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root=configuration["ModelLab:Root"];
        if(string.IsNullOrWhiteSpace(root))
            root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PersonalAI","ModelLab");
        root=Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root); return root;
    }
}
