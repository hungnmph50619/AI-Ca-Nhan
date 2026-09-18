using System.Collections.Concurrent;
using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ITaskEngineService
{
    Task<PersonalTask> CreateAsync(
        string goal,
        CancellationToken cancellationToken = default);

    PersonalTask Prepare(PreparePersonalTaskRequest request);

    PersonalTaskListResponse GetAll();

    PersonalTask? Get(Guid taskId);

    Task<PersonalTaskStepExecutionResponse?> ExecuteNextAsync(
        Guid taskId,
        bool confirmed,
        CancellationToken cancellationToken = default);

    Task<PersonalTask?> CancelAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);
}

public sealed class PersonalTaskValidationException(string message) : Exception(message);

public sealed class PersonalTaskConfirmationRequiredException(string message) : Exception(message);

public sealed class TaskEngineService : ITaskEngineService
{
    public const int MaximumTasks = 50;
    public const int MaximumSteps = 8;
    public const int MaximumGoalCharacters = 2_000;
    public const int MaximumPlanCharacters = 2_000;

    private static readonly ConcurrentDictionary<Guid, PersonalTask> Tasks = new();
    private static readonly SemaphoreSlim MutationGate = new(1, 1);

    private readonly IToolRegistry _registry;
    private readonly IToolInputValidator _validator;
    private readonly IToolExecutionService _executor;
    private readonly IToolResultSynthesisService _synthesizer;
    private readonly IAiProviderResolver _providerResolver;
    private readonly IToolActivityStore _activityStore;
    private readonly ILogger<TaskEngineService> _logger;

    public TaskEngineService(
        IToolRegistry registry,
        IToolInputValidator validator,
        IToolExecutionService executor,
        IToolResultSynthesisService synthesizer,
        IAiProviderResolver providerResolver,
        IToolActivityStore activityStore,
        ILogger<TaskEngineService> logger)
    {
        _registry = registry;
        _validator = validator;
        _executor = executor;
        _synthesizer = synthesizer;
        _providerResolver = providerResolver;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task<PersonalTask> CreateAsync(
        string goal,
        CancellationToken cancellationToken = default)
    {
        goal = (goal ?? string.Empty).Trim();
        if (goal.Length < 3)
        {
            throw new PersonalTaskValidationException(
                "Mục tiêu cần có ít nhất 3 ký tự.");
        }

        if (goal.Length > MaximumGoalCharacters)
        {
            throw new PersonalTaskValidationException(
                $"Mục tiêu không được dài hơn {MaximumGoalCharacters:N0} ký tự.");
        }

        var provider = _providerResolver.GetActive();
        var definitions = _registry.GetAll();
        var prompt = BuildPlanningPrompt(goal, definitions);
        var answer = await provider.ReplyAsync(
            [new ChatMessage("user", prompt)],
            cancellationToken);

        var parsed = ParsePlan(answer);
        if (parsed.Steps.Count == 0)
        {
            throw new PersonalTaskValidationException(
                string.IsNullOrWhiteSpace(parsed.Plan)
                    ? "AI chưa tạo được kế hoạch có thể thực thi bằng các công cụ hiện có."
                    : parsed.Plan);
        }

        var task = new PersonalTask(
            Guid.NewGuid(),
            goal,
            PersonalTaskStatuses.Planned,
            parsed.Plan,
            parsed.Steps,
            1,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            provider.Name,
            provider.Model);

        Tasks[task.Id] = task;
        TrimTasks();
        return task;
    }

    public PersonalTask Prepare(PreparePersonalTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var goal = (request.Goal ?? string.Empty).Trim();
        if (goal.Length < 3)
        {
            throw new PersonalTaskValidationException(
                "Mục tiêu cần có ít nhất 3 ký tự.");
        }

        if (goal.Length > MaximumGoalCharacters)
        {
            throw new PersonalTaskValidationException(
                $"Mục tiêu không được dài hơn {MaximumGoalCharacters:N0} ký tự.");
        }

        var plan = (request.Plan ?? string.Empty).Trim();
        if (plan.Length == 0)
        {
            throw new PersonalTaskValidationException(
                "Kế hoạch tác vụ không được để trống.");
        }

        if (plan.Length > MaximumPlanCharacters)
        {
            plan = plan[..MaximumPlanCharacters];
        }

        var drafts = request.Steps ?? [];
        if (drafts.Count == 0)
        {
            throw new PersonalTaskValidationException(
                "Kế hoạch phải có ít nhất một bước.");
        }

        if (drafts.Count > MaximumSteps)
        {
            throw new PersonalTaskValidationException(
                $"Mỗi tác vụ hiện hỗ trợ tối đa {MaximumSteps} bước.");
        }

        var steps = new List<PersonalTaskStep>(drafts.Count);
        for (var index = 0; index < drafts.Count; index++)
        {
            var draft = drafts[index];
            var toolName = (draft.ToolName ?? string.Empty).Trim();
            if (!_registry.TryGet(toolName, out var tool) || tool is null)
            {
                throw InvalidStep(
                    index + 1,
                    "dùng công cụ không có trong danh mục hiện tại.");
            }

            if (draft.Arguments.ValueKind != JsonValueKind.Object)
            {
                throw InvalidStep(
                    index + 1,
                    "không có tham số công cụ hợp lệ.");
            }

            var validation = _validator.Validate(
                tool.Definition.InputSchema,
                draft.Arguments);
            if (!validation.IsValid)
            {
                throw InvalidStep(
                    index + 1,
                    string.Join(" ", validation.Errors));
            }

            var title = string.IsNullOrWhiteSpace(draft.Title)
                ? $"Bước {index + 1}"
                : draft.Title.Trim();
            var description = string.IsNullOrWhiteSpace(draft.Description)
                ? title
                : draft.Description.Trim();
            if (title.Length > 160)
            {
                title = title[..160];
            }
            if (description.Length > 500)
            {
                description = description[..500];
            }

            var requiresConfirmation = tool.Definition.RequiresConfirmation
                || tool.Definition.RequiredPermissions.Any(
                    ToolPermissions.RequiresExplicitConfirmation);

            steps.Add(new PersonalTaskStep(
                index + 1,
                title,
                description,
                tool.Definition.Name,
                draft.Arguments.Clone(),
                tool.Definition.RequiredPermissions.ToArray(),
                requiresConfirmation,
                PersonalTaskStepStatuses.Pending));
        }

        var task = new PersonalTask(
            Guid.NewGuid(),
            goal,
            PersonalTaskStatuses.Planned,
            plan,
            steps,
            1,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            "Máy chủ",
            "Kế hoạch cấu trúc");

        Tasks[task.Id] = task;
        TrimTasks();
        return task;
    }

    public PersonalTaskListResponse GetAll()
    {
        var tasks = Tasks.Values
            .OrderByDescending(task => task.CreatedAt)
            .ToArray();

        return new PersonalTaskListResponse(
            Persistent: false,
            MaximumTasks,
            tasks);
    }

    public PersonalTask? Get(Guid taskId) =>
        Tasks.TryGetValue(taskId, out var task) ? task : null;

    public async Task<PersonalTaskStepExecutionResponse?> ExecuteNextAsync(
        Guid taskId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            if (!Tasks.TryGetValue(taskId, out var task))
            {
                return null;
            }

            if (task.Status is PersonalTaskStatuses.Completed
                or PersonalTaskStatuses.Cancelled
                or PersonalTaskStatuses.Failed)
            {
                throw new PersonalTaskValidationException(
                    "Tác vụ này đã kết thúc nên không thể chạy thêm bước.");
            }

            var position = task.CurrentStep - 1;
            if (position < 0 || position >= task.Steps.Count)
            {
                throw new PersonalTaskValidationException(
                    "Tác vụ không còn bước hợp lệ để thực thi.");
            }

            var step = task.Steps[position];
            if (!_registry.TryGet(step.ToolName, out var tool) || tool is null)
            {
                throw new PersonalTaskValidationException(
                    $"Công cụ của bước {step.Index} không còn tồn tại trong danh mục.");
            }

            var validation = _validator.Validate(
                tool.Definition.InputSchema,
                step.Arguments);
            if (!validation.IsValid)
            {
                throw new PersonalTaskValidationException(
                    $"Bước {step.Index} không còn hợp lệ: {string.Join(" ", validation.Errors)}");
            }

            if (step.RequiresConfirmation && !confirmed)
            {
                TryRecordActivity(new ToolActivityEvent(
                    ToolActivityEventTypes.TaskStepExecutionDenied,
                    step.ToolName,
                    null,
                    null,
                    "task-engine",
                    task.PlanningProvider,
                    task.PlanningModel,
                    step.RequiredPermissions,
                    false,
                    null,
                    ToolExecutionStatuses.Denied,
                    step.Arguments));

                throw new PersonalTaskConfirmationRequiredException(
                    $"Bước {step.Index} cần xác nhận rõ ràng trước khi thực thi.");
            }

            var startedAt = DateTimeOffset.UtcNow;
            var runningStep = step with
            {
                Status = PersonalTaskStepStatuses.Running,
                StartedAt = startedAt
            };
            var runningSteps = ReplaceStep(task.Steps, position, runningStep);
            var runningTask = task with
            {
                Status = PersonalTaskStatuses.Running,
                Steps = runningSteps,
                StartedAt = task.StartedAt ?? startedAt
            };
            Tasks[taskId] = runningTask;

            ToolExecutionResponse execution;
            try
            {
                execution = await _executor.ExecuteAsync(
                    new ToolExecutionRequest(
                        step.ToolName,
                        step.Arguments,
                        tool.Definition.RequiredPermissions,
                        Confirmed: confirmed),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                var revertedStep = runningStep with
                {
                    Status = PersonalTaskStepStatuses.Pending,
                    StartedAt = null
                };
                Tasks[taskId] = runningTask with
                {
                    Status = task.Status,
                    Steps = ReplaceStep(runningSteps, position, revertedStep),
                    StartedAt = task.StartedAt
                };
                throw;
            }

            var proposal = new ToolCallProposal(
                Guid.NewGuid(),
                step.ToolName,
                step.Arguments,
                $"Bước {step.Index} của tác vụ: {step.Title}",
                $"Thực thi bước {step.Index} của tác vụ.",
                step.RequiredPermissions,
                step.RequiresConfirmation,
                DateTimeOffset.UtcNow,
                "task-engine",
                task.PlanningProvider,
                task.PlanningModel);

            var localSummary = _synthesizer.CreateLocalSummary(
                proposal,
                execution);

            TryRecordActivity(new ToolActivityEvent(
                ToolActivityEventTypes.TaskStepExecutionCompleted,
                step.ToolName,
                null,
                execution.InvocationId,
                "task-engine",
                task.PlanningProvider,
                task.PlanningModel,
                step.RequiredPermissions,
                confirmed,
                null,
                execution.Status,
                step.Arguments,
                execution.Output,
                execution.DurationMs));

            var completedAt = DateTimeOffset.UtcNow;
            var finishedStep = runningStep with
            {
                Status = execution.Success
                    ? PersonalTaskStepStatuses.Completed
                    : PersonalTaskStepStatuses.Failed,
                InvocationId = execution.InvocationId,
                LocalSummary = localSummary,
                CompletedAt = completedAt
            };
            var finishedSteps = ReplaceStep(runningSteps, position, finishedStep);

            PersonalTask finishedTask;
            if (!execution.Success)
            {
                finishedTask = runningTask with
                {
                    Status = PersonalTaskStatuses.Failed,
                    Steps = finishedSteps,
                    CompletedAt = completedAt,
                    Result = localSummary
                };
            }
            else if (position == finishedSteps.Count - 1)
            {
                finishedTask = runningTask with
                {
                    Status = PersonalTaskStatuses.Completed,
                    Steps = finishedSteps,
                    CurrentStep = finishedSteps.Count,
                    CompletedAt = completedAt,
                    Result = BuildTaskResult(finishedSteps)
                };
            }
            else
            {
                finishedTask = runningTask with
                {
                    Status = PersonalTaskStatuses.Running,
                    Steps = finishedSteps,
                    CurrentStep = task.CurrentStep + 1
                };
            }

            Tasks[taskId] = finishedTask;
            return new PersonalTaskStepExecutionResponse(
                finishedTask,
                execution,
                localSummary);
        }
        finally
        {
            MutationGate.Release();
        }
    }

    public async Task<PersonalTask?> CancelAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            if (!Tasks.TryGetValue(taskId, out var task))
            {
                return null;
            }

            if (task.Status is PersonalTaskStatuses.Completed
                or PersonalTaskStatuses.Failed
                or PersonalTaskStatuses.Cancelled)
            {
                return task;
            }

            var cancelled = task with
            {
                Status = PersonalTaskStatuses.Cancelled,
                CompletedAt = DateTimeOffset.UtcNow,
                Result = "Tác vụ đã được người dùng hủy."
            };
            Tasks[taskId] = cancelled;
            return cancelled;
        }
        finally
        {
            MutationGate.Release();
        }
    }

    private ParsedTaskPlan ParsePlan(string rawAnswer)
    {
        var json = ExtractJsonObject(rawAnswer);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "Task planner returned invalid JSON.");
            throw new PersonalTaskValidationException(
                "AI chưa tạo được kế hoạch có cấu trúc hợp lệ. Hãy diễn đạt mục tiêu cụ thể hơn rồi thử lại.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new PersonalTaskValidationException(
                    "Kế hoạch AI trả về không đúng cấu trúc yêu cầu.");
            }

            var plan = ReadText(root, "plan", "Kế hoạch tác vụ");
            if (plan.Length > MaximumPlanCharacters)
            {
                plan = plan[..MaximumPlanCharacters];
            }

            if (!root.TryGetProperty("steps", out var stepsElement)
                || stepsElement.ValueKind != JsonValueKind.Array)
            {
                throw new PersonalTaskValidationException(
                    "Kế hoạch AI không có danh sách bước hợp lệ.");
            }

            var rawSteps = stepsElement.EnumerateArray().ToArray();
            if (rawSteps.Length == 0)
            {
                return new ParsedTaskPlan(plan, []);
            }

            if (rawSteps.Length > MaximumSteps)
            {
                throw new PersonalTaskValidationException(
                    $"Mỗi tác vụ hiện hỗ trợ tối đa {MaximumSteps} bước.");
            }

            var steps = new List<PersonalTaskStep>(rawSteps.Length);
            for (var index = 0; index < rawSteps.Length; index++)
            {
                var item = rawSteps[index];
                if (item.ValueKind != JsonValueKind.Object)
                {
                    throw InvalidStep(index + 1, "không đúng cấu trúc.");
                }

                var title = ReadText(item, "title", $"Bước {index + 1}");
                var description = ReadText(item, "description", title);
                var toolName = ReadText(item, "toolName", string.Empty);
                if (title.Length > 160)
                {
                    title = title[..160];
                }
                if (description.Length > 500)
                {
                    description = description[..500];
                }

                if (!_registry.TryGet(toolName, out var tool) || tool is null)
                {
                    throw InvalidStep(
                        index + 1,
                        "dùng công cụ không có trong danh mục hiện tại.");
                }

                if (!item.TryGetProperty("arguments", out var arguments)
                    || arguments.ValueKind != JsonValueKind.Object)
                {
                    throw InvalidStep(
                        index + 1,
                        "không có tham số công cụ hợp lệ.");
                }

                var validation = _validator.Validate(
                    tool.Definition.InputSchema,
                    arguments);
                if (!validation.IsValid)
                {
                    throw InvalidStep(
                        index + 1,
                        string.Join(" ", validation.Errors));
                }

                var requiresConfirmation = tool.Definition.RequiresConfirmation
                    || tool.Definition.RequiredPermissions.Any(
                        ToolPermissions.RequiresExplicitConfirmation);

                steps.Add(new PersonalTaskStep(
                    index + 1,
                    title,
                    description,
                    tool.Definition.Name,
                    arguments.Clone(),
                    tool.Definition.RequiredPermissions.ToArray(),
                    requiresConfirmation,
                    PersonalTaskStepStatuses.Pending));
            }

            return new ParsedTaskPlan(plan, steps);
        }
    }

    private static string BuildPlanningPrompt(
        string goal,
        IReadOnlyList<ToolDefinition> definitions)
    {
        var catalog = definitions.Select(definition => new
        {
            name = definition.Name,
            description = definition.Description,
            permissions = definition.RequiredPermissions,
            requiresConfirmation = definition.RequiresConfirmation
                || definition.RequiredPermissions.Any(
                    ToolPermissions.RequiresExplicitConfirmation),
            inputSchema = JsonDocument.Parse(
                definition.InputSchema.GetRawText()).RootElement
        });

        var catalogJson = JsonSerializer.Serialize(catalog);
        return $$"""
Bạn là bộ lập kế hoạch tác vụ của PersonalAI.

Mục tiêu của người dùng:
{{goal}}

Danh mục công cụ hiện có:
{{catalogJson}}

Hãy tạo một kế hoạch có thể thực thi tuần tự bằng các công cụ trên.

Quy tắc bắt buộc:
- Chỉ dùng đúng tên công cụ có trong danh mục.
- Mỗi bước chỉ được dùng một công cụ.
- Tối đa {{MaximumSteps}} bước.
- Không tự giả định một thao tác đã xảy ra.
- Không được bỏ qua quyền hoặc yêu cầu xác nhận của công cụ.
- Nếu mục tiêu không thể hoàn thành bằng danh mục hiện có, trả steps là mảng rỗng và giải thích ngắn trong plan.
- Chỉ trả một đối tượng JSON thuần, không dùng Markdown hay khối mã.
- Cấu trúc chính xác:
{"plan":"Tóm tắt kế hoạch bằng tiếng Việt", "steps":[{"title":"Tên bước bằng tiếng Việt", "description":"Mô tả ngắn bằng tiếng Việt", "toolName":"tên.công_cụ", "arguments":{ } } ] }
""";
    }

    private static string ExtractJsonObject(string value)
    {
        value = (value ?? string.Empty).Trim();
        var first = value.IndexOf('{');
        var last = value.LastIndexOf('}');
        if (first < 0 || last <= first)
        {
            throw new PersonalTaskValidationException(
                "AI chưa trả về kế hoạch có cấu trúc JSON hợp lệ.");
        }

        return value[first..(last + 1)];
    }

    private static string ReadText(
        JsonElement element,
        string propertyName,
        string fallback)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return fallback;
        }

        var value = property.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static PersonalTaskValidationException InvalidStep(
        int index,
        string detail) =>
        new($"Bước {index} trong kế hoạch AI không hợp lệ: {detail}");

    private static IReadOnlyList<PersonalTaskStep> ReplaceStep(
        IReadOnlyList<PersonalTaskStep> steps,
        int position,
        PersonalTaskStep replacement)
    {
        var copy = steps.ToArray();
        copy[position] = replacement;
        return copy;
    }

    private static string BuildTaskResult(
        IReadOnlyList<PersonalTaskStep> steps)
    {
        var result = string.Join(
            Environment.NewLine,
            steps
                .Where(step => !string.IsNullOrWhiteSpace(step.LocalSummary))
                .Select(step => $"{step.Index}. {step.LocalSummary}"));

        return result.Length <= 4_000
            ? result
            : result[..4_000].TrimEnd() + "…";
    }

    private static void TrimTasks()
    {
        if (Tasks.Count <= MaximumTasks)
        {
            return;
        }

        foreach (var task in Tasks.Values
                     .OrderBy(item => item.CreatedAt)
                     .Take(Tasks.Count - MaximumTasks))
        {
            Tasks.TryRemove(task.Id, out _);
        }
    }

    private void TryRecordActivity(ToolActivityEvent activity)
    {
        try
        {
            _activityStore.Record(activity);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Không thể ghi nhật ký hoạt động bước tác vụ.");
        }
    }

    private sealed record ParsedTaskPlan(
        string Plan,
        IReadOnlyList<PersonalTaskStep> Steps);
}
