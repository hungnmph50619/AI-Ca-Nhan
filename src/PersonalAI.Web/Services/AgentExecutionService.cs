using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IAgentExecutionService
{
    AgentExecutionTask Create(string goal);
    IReadOnlyCollection<AgentExecutionTask> GetAll();
}

public sealed class AgentExecutionService : IAgentExecutionService
{
    private readonly List<AgentExecutionTask> _tasks = new();

    public AgentExecutionTask Create(string goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
            throw new ArgumentException("Mục tiêu thực thi không được để trống.");

        var task = new AgentExecutionTask(
            Guid.NewGuid(),
            goal.Trim(),
            AgentExecutionStatuses.Pending,
            DateTimeOffset.UtcNow);

        _tasks.Add(task);
        return task;
    }

    public IReadOnlyCollection<AgentExecutionTask> GetAll() => _tasks.AsReadOnly();
}
