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

    Task<PersonalTask?> ResumeAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);

    PersonalTaskRetryAssessment? GetRetryAssessment(Guid taskId);

    Task<PersonalTaskRetryResponse?> RetryFailedStepAsync(
        Guid taskId,
        bool confirmedReview,
        CancellationToken cancellationToken = default);
}

public sealed class PersonalTaskValidationException(string message) : Exception(message);

public sealed class PersonalTaskConfirmationRequiredException(string message) : Exception(message);

public sealed class PersonalTaskRetryBlockedException(string message) : Exception(message);

public sealed class TaskEngineService : ITaskEngineService
{
    public const int MaximumTasks = 50;
    public const int MaximumSteps = 8;
    public const int MaximumGoalCharacters = 2_000;
    public const int MaximumPlanCharacters = 2_000;

    private static readonly SemaphoreSlim MutationGate = new(1, 1);

    private readonly IToolRegistry _registry;
    private readonly IToolInputValidator _validator;
    private readonly IToolExecutionService _executor;
    private readonly IToolResultSynthesisService _synthesizer;
    private readonly IAiProviderResolver _providerResolver;
    private readonly IToolActivityStore _activityStore;
    private readonly IPersonalTaskStore _taskStore;
    private readonly ILogger<TaskEngineService> _logger;

    public TaskEngineService(
        IToolRegistry registry,
        IToolInputValidator validator,
        IToolExecutionService executor,
        IToolResultSynthesisService synthesizer,
        IAiProviderResolver providerResolver,
        IToolActivityStore activityStore,
        IPersonalTaskStore taskStore,
        ILogger<TaskEngineService> logger)
    {
        _registry = registry;
        _validator = validator;
        _executor = executor;
        _synthesizer = synthesizer;
        _providerResolver = providerResolver;
        _activityStore = activityStore;
        _taskStore = taskStore;
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

        return _taskStore.Save(task);
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
            var dependsOn = NormalizeDependencies(
                draft.DependsOn,
                index + 1);

            steps.Add(new PersonalTaskStep(
                index + 1,
                title,
                description,
                tool.Definition.Name,
                draft.Arguments.Clone(),
                tool.Definition.RequiredPermissions.ToArray(),
                requiresConfirmation,
                PersonalTaskStepStatuses.Pending,
                DependsOn: dependsOn));
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

        return _taskStore.Save(task);
    }

    public PersonalTaskListResponse GetAll()
    {
        var tasks = _taskStore.GetAll();

        return new PersonalTaskListResponse(
            Persistent: true,
            MaximumTasks,
            tasks);
    }

    public PersonalTask? Get(Guid taskId) =>
        _taskStore.Get(taskId);

    public async Task<PersonalTaskStepExecutionResponse?> ExecuteNextAsync(
        Guid taskId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            var task = _taskStore.Get(taskId);
            if (task is null)
            {
                return null;
            }

            if (task.Status == PersonalTaskStatuses.Interrupted)
            {
                throw new PersonalTaskValidationException(
                    "Tác vụ bị gián đoạn trong lúc một bước đang chạy. Hãy kiểm tra trạng thái thực tế rồi khôi phục tác vụ trước khi tiếp tục.");
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
            EnsureDependenciesSatisfied(task, step);

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
                StartedAt = startedAt,
                AttemptCount = step.AttemptCount + 1
            };
            var runningSteps = ReplaceStep(task.Steps, position, runningStep);
            var runningTask = task with
            {
                Status = PersonalTaskStatuses.Running,
                Steps = runningSteps,
                StartedAt = task.StartedAt ?? startedAt
            };
            _taskStore.Save(runningTask);

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
                _taskStore.Save(runningTask with
                {
                    Status = task.Status,
                    Steps = ReplaceStep(runningSteps, position, revertedStep),
                    StartedAt = task.StartedAt
                });
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

            _taskStore.Save(finishedTask);
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
            var task = _taskStore.Get(taskId);
            if (task is null)
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
            return _taskStore.Save(cancelled);
        }
        finally
        {
            MutationGate.Release();
        }
    }

    public async Task<PersonalTask?> ResumeAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            var task = _taskStore.Get(taskId);
            if (task is null)
            {
                return null;
            }

            if (task.Status != PersonalTaskStatuses.Interrupted)
            {
                return task;
            }

            var position = task.CurrentStep - 1;
            if (position < 0 || position >= task.Steps.Count)
            {
                throw new PersonalTaskValidationException(
                    "Tác vụ bị gián đoạn nhưng không còn bước hợp lệ để khôi phục.");
            }

            var step = task.Steps[position];
            if (step.Status != PersonalTaskStepStatuses.Interrupted)
            {
                throw new PersonalTaskValidationException(
                    "Không tìm thấy bước bị gián đoạn để khôi phục.");
            }

            var resumedStep = step with
            {
                Status = PersonalTaskStepStatuses.Pending,
                InvocationId = null,
                LocalSummary = null,
                StartedAt = null,
                CompletedAt = null
            };
            var resumedSteps = ReplaceStep(task.Steps, position, resumedStep);
            var resumedTask = task with
            {
                Status = task.StartedAt is null
                    ? PersonalTaskStatuses.Planned
                    : PersonalTaskStatuses.Running,
                Steps = resumedSteps,
                CompletedAt = null,
                Result = null
            };

            return _taskStore.Save(resumedTask);
        }
        finally
        {
            MutationGate.Release();
        }
    }

    public PersonalTaskRetryAssessment? GetRetryAssessment(Guid taskId)
    {
        var task = _taskStore.Get(taskId);
        return task is null ? null : AssessRetry(task);
    }

    public async Task<PersonalTaskRetryResponse?> RetryFailedStepAsync(
        Guid taskId,
        bool confirmedReview,
        CancellationToken cancellationToken = default)
    {
        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            var task = _taskStore.Get(taskId);
            if (task is null)
            {
                return null;
            }

            var assessment = AssessRetry(task);
            if (!assessment.CanRetry)
            {
                throw new PersonalTaskRetryBlockedException(assessment.Message);
            }

            if (assessment.RequiresReviewConfirmation && !confirmedReview)
            {
                throw new PersonalTaskConfirmationRequiredException(
                    assessment.Message + " Hãy xác nhận sau khi bạn đã kiểm tra trạng thái thực tế.");
            }

            var position = task.CurrentStep - 1;
            var step = task.Steps[position];
            var retriedStep = step with
            {
                Status = PersonalTaskStepStatuses.Pending,
                InvocationId = null,
                LocalSummary = null,
                StartedAt = null,
                CompletedAt = null
            };
            var retriedSteps = ReplaceStep(task.Steps, position, retriedStep);
            var retriedTask = task with
            {
                Status = task.StartedAt is null
                    ? PersonalTaskStatuses.Planned
                    : PersonalTaskStatuses.Running,
                Steps = retriedSteps,
                CompletedAt = null,
                Result = null
            };

            var saved = _taskStore.Save(retriedTask);

            TryRecordActivity(new ToolActivityEvent(
                ToolActivityEventTypes.TaskStepRetryPrepared,
                step.ToolName,
                null,
                step.InvocationId,
                "task-engine",
                task.PlanningProvider,
                task.PlanningModel,
                step.RequiredPermissions,
                confirmedReview,
                null,
                "retry-prepared",
                step.Arguments));

            return new PersonalTaskRetryResponse(saved, assessment);
        }
        finally
        {
            MutationGate.Release();
        }
    }

    private PersonalTaskRetryAssessment AssessRetry(PersonalTask task)
    {
        var position = task.CurrentStep - 1;
        if (task.Status != PersonalTaskStatuses.Failed
            || position < 0
            || position >= task.Steps.Count)
        {
            return BlockedRetry(
                task,
                position,
                "Chỉ có thể chuẩn bị thử lại khi tác vụ đang ở trạng thái có lỗi tại một bước cụ thể.");
        }

        var step = task.Steps[position];
        if (step.Status != PersonalTaskStepStatuses.Failed)
        {
            return BlockedRetry(
                task,
                position,
                "Bước hiện tại không ở trạng thái có lỗi nên không thể chuẩn bị thử lại.");
        }

        if (!_registry.TryGet(step.ToolName, out var tool) || tool is null)
        {
            return BlockedRetry(
                task,
                position,
                "Công cụ của bước này không còn tồn tại trong danh mục hiện tại.");
        }

        var validation = _validator.Validate(
            tool.Definition.InputSchema,
            step.Arguments);
        if (!validation.IsValid)
        {
            return BlockedRetry(
                task,
                position,
                "Tham số của bước không còn hợp lệ với công cụ hiện tại nên không thể thử lại.");
        }

        var permissions = tool.Definition.RequiredPermissions;
        if (permissions.Any(permission =>
                permission.Equals(ToolPermissions.External, StringComparison.OrdinalIgnoreCase)
                || permission.Equals(ToolPermissions.Sensitive, StringComparison.OrdinalIgnoreCase)))
        {
            return BlockedRetry(
                task,
                position,
                "Bước dùng quyền BÊN NGOÀI hoặc NHẠY CẢM nên phiên bản này không cho thử lại tự động.");
        }

        var readOnly = permissions.Count > 0
            && permissions.All(permission =>
                permission.Equals(ToolPermissions.Read, StringComparison.OrdinalIgnoreCase))
            && !tool.Definition.RequiresConfirmation;
        if (readOnly)
        {
            return new PersonalTaskRetryAssessment(
                task.Id,
                step.Index,
                step.ToolName,
                PersonalTaskRetrySafety.Safe,
                true,
                false,
                "Bước chỉ đọc có thể được đưa về trạng thái chờ để bạn chạy lại. Thao tác thử lại không tự thực thi công cụ.");
        }

        if (step.ToolName.Equals("workspace.create_directory", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewRetry(
                task,
                step,
                "Bước tạo thư mục có thể thử lại sau khi kiểm tra trạng thái thực tế. Nếu thư mục đã được tạo ở lần trước, lần chạy sau sẽ không tạo thêm một bản sao.");
        }

        if (step.ToolName.Equals("workspace.write_text", StringComparison.OrdinalIgnoreCase))
        {
            var mode = ReadArgumentText(step.Arguments, "mode").ToLowerInvariant();
            var expectedSha256 = ReadArgumentText(step.Arguments, "expectedSha256");

            if (mode == "append")
            {
                return BlockedRetry(
                    task,
                    position,
                    "Không cho thử lại bước nối thêm văn bản vì lần chạy trước có thể đã ghi dữ liệu; chạy lại có thể làm nội dung bị lặp.");
            }

            if (mode == "create")
            {
                return ReviewRetry(
                    task,
                    step,
                    "Bước tạo tệp có thể thử lại sau khi kiểm tra tệp đích. Nếu lần trước đã tạo tệp, công cụ sẽ từ chối tạo trùng.");
            }

            if (mode == "overwrite" && expectedSha256.Length == 64)
            {
                return ReviewRetry(
                    task,
                    step,
                    "Bước ghi đè có mã băm SHA-256 kỳ vọng nên có thể chuẩn bị thử lại sau khi kiểm tra tệp hiện tại. Mã băm sẽ giúp chặn ghi đè nhầm phiên bản đã thay đổi.");
            }

            return BlockedRetry(
                task,
                position,
                "Không cho thử lại bước ghi đè khi không có mã băm SHA-256 kỳ vọng vì có thể ghi đè dữ liệu mới hơn.");
        }

        if (step.ToolName.Equals("workspace.move", StringComparison.OrdinalIgnoreCase))
        {
            var expectedSha256 = ReadArgumentText(step.Arguments, "expectedSha256");
            return expectedSha256.Length == 64
                ? ReviewRetry(
                    task,
                    step,
                    "Bước di chuyển tệp có mã băm SHA-256 kỳ vọng nên có thể chuẩn bị thử lại sau khi kiểm tra cả đường dẫn nguồn và đích.")
                : BlockedRetry(
                    task,
                    position,
                    "Không cho thử lại bước di chuyển khi không có mã băm SHA-256 kỳ vọng vì trạng thái nguồn và đích có thể đã thay đổi.");
        }

        if (step.ToolName.Equals("workspace.delete", StringComparison.OrdinalIgnoreCase))
        {
            var expectedSha256 = ReadArgumentText(step.Arguments, "expectedSha256");
            return expectedSha256.Length == 64
                ? ReviewRetry(
                    task,
                    step,
                    "Bước xóa tệp có mã băm SHA-256 kỳ vọng nên chỉ được chuẩn bị thử lại sau khi bạn kiểm tra tệp hiện tại và xác nhận lại.")
                : BlockedRetry(
                    task,
                    position,
                    "Không cho thử lại bước xóa khi không có mã băm SHA-256 kỳ vọng. Hãy kiểm tra trạng thái thực tế và tạo tác vụ mới nếu vẫn muốn xóa.");
        }

        return BlockedRetry(
            task,
            position,
            "Công cụ của bước này chưa có chính sách thử lại an toàn trong phiên bản hiện tại.");
    }

    private static PersonalTaskRetryAssessment ReviewRetry(
        PersonalTask task,
        PersonalTaskStep step,
        string message) =>
        new(
            task.Id,
            step.Index,
            step.ToolName,
            PersonalTaskRetrySafety.ReviewRequired,
            true,
            true,
            message + " Chuẩn bị thử lại chỉ đưa bước về trạng thái chờ; công cụ chưa được chạy.");

    private static PersonalTaskRetryAssessment BlockedRetry(
        PersonalTask task,
        int position,
        string message)
    {
        var step = position >= 0 && position < task.Steps.Count
            ? task.Steps[position]
            : null;

        return new PersonalTaskRetryAssessment(
            task.Id,
            step?.Index ?? task.CurrentStep,
            step?.ToolName ?? string.Empty,
            PersonalTaskRetrySafety.Blocked,
            false,
            false,
            message);
    }

    private static string ReadArgumentText(
        JsonElement arguments,
        string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return property.GetString()?.Trim() ?? string.Empty;
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
                var dependsOn = ReadDependencies(
                    item,
                    index + 1);

                steps.Add(new PersonalTaskStep(
                    index + 1,
                    title,
                    description,
                    tool.Definition.Name,
                    arguments.Clone(),
                    tool.Definition.RequiredPermissions.ToArray(),
                    requiresConfirmation,
                    PersonalTaskStepStatuses.Pending,
                    DependsOn: dependsOn));
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

Hãy tạo một kế hoạch có thể thực thi bằng các công cụ trên và khai báo rõ quan hệ phụ thuộc giữa các bước.

Quy tắc bắt buộc:
- Chỉ dùng đúng tên công cụ có trong danh mục.
- Mỗi bước chỉ được dùng một công cụ.
- Tối đa {{MaximumSteps}} bước.
- Mỗi bước có "dependsOn" là mảng số thứ tự các bước trước mà nó thực sự cần.
- Bước 1 luôn có dependsOn rỗng. Một bước chỉ được phụ thuộc vào bước có số nhỏ hơn chính nó.
- Không tạo vòng phụ thuộc và không thêm phụ thuộc không cần thiết.
- Không tự giả định một thao tác đã xảy ra.
- Không được bỏ qua quyền hoặc yêu cầu xác nhận của công cụ.
- Nếu mục tiêu không thể hoàn thành bằng danh mục hiện có, trả steps là mảng rỗng và giải thích ngắn trong plan.
- Chỉ trả một đối tượng JSON thuần, không dùng Markdown hay khối mã.
- Cấu trúc chính xác:
{"plan":"Tóm tắt kế hoạch bằng tiếng Việt", "steps":[{"title":"Tên bước bằng tiếng Việt", "description":"Mô tả ngắn bằng tiếng Việt", "toolName":"tên.công_cụ", "arguments":{ }, "dependsOn":[] } ] }
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

    private static IReadOnlyList<int> ReadDependencies(
        JsonElement element,
        int stepIndex)
    {
        if (!element.TryGetProperty("dependsOn", out var property)
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            throw InvalidStep(
                stepIndex,
                "dependsOn phải là một mảng số thứ tự bước.");
        }

        var dependencies = new List<int>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number
                || !item.TryGetInt32(out var dependency))
            {
                throw InvalidStep(
                    stepIndex,
                    "dependsOn chỉ được chứa số nguyên.");
            }

            dependencies.Add(dependency);
        }

        return NormalizeDependencies(dependencies, stepIndex);
    }

    private static IReadOnlyList<int> NormalizeDependencies(
        IReadOnlyList<int>? dependencies,
        int stepIndex)
    {
        var normalized = (dependencies ?? [])
            .Distinct()
            .OrderBy(value => value)
            .ToArray();

        if (normalized.Length == 0)
        {
            return normalized;
        }

        if (stepIndex <= 1)
        {
            throw InvalidStep(
                stepIndex,
                "bước đầu tiên không thể phụ thuộc vào bước khác.");
        }

        if (normalized.Any(value => value <= 0 || value >= stepIndex))
        {
            throw InvalidStep(
                stepIndex,
                $"dependsOn chỉ được tham chiếu các bước từ 1 đến {stepIndex - 1}.");
        }

        return normalized;
    }

    private static void EnsureDependenciesSatisfied(
        PersonalTask task,
        PersonalTaskStep step)
    {
        var dependencies = step.DependsOn ?? [];
        if (dependencies.Count == 0)
        {
            return;
        }

        var incomplete = dependencies
            .Where(dependency =>
            {
                var requiredStep = task.Steps.FirstOrDefault(
                    candidate => candidate.Index == dependency);
                return requiredStep is null
                    || requiredStep.Status != PersonalTaskStepStatuses.Completed;
            })
            .ToArray();

        if (incomplete.Length == 0)
        {
            return;
        }

        throw new PersonalTaskValidationException(
            $"Bước {step.Index} đang chờ bước {string.Join(", ", incomplete)} hoàn tất. PersonalAI sẽ không bỏ qua quan hệ phụ thuộc.");
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
