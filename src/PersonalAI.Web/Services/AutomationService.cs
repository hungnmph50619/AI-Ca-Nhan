using Microsoft.AspNetCore.Http;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IAutomationService
{
    AutomationStatusResponse GetStatus();

    AutomationListResponse GetAll();

    PersonalAutomation? Get(Guid automationId);

    PersonalAutomation Create(
        CreateAutomationRequest request);

    PersonalAutomation? SetEnabled(
        Guid automationId,
        SetAutomationEnabledRequest request);

    PersonalAutomation? Resume(
        Guid automationId,
        bool confirmed);

    Task<AutomationRunResult?> RunNowAsync(
        Guid automationId,
        bool confirmed,
        CancellationToken cancellationToken = default);

    bool Delete(
        Guid automationId,
        bool confirmed);
}

public sealed class AutomationConfirmationRequiredException(string message)
    : Exception(message);

public sealed class AutomationValidationException(string message)
    : Exception(message);

public sealed class AutomationService(
    IAutomationStore store,
    ITaskEngineService taskEngine,
    IWorkspaceContextAccessor workspaceContext,
    IAutomationCoordinator coordinator,
    IAuditRecorder audit) : IAutomationService
{
    public const int MinimumIntervalMinutes = 5;
    public const int MaximumIntervalMinutes = 10_080;
    public const int SchedulerPollSeconds = 30;
    public const int MaximumDueBatchSize = 20;
    public const int MaximumNameCharacters = 80;

    public AutomationStatusResponse GetStatus() =>
        new(
            PersonalAiRelease.Version,
            Supported: true,
            BackgroundSchedulerEnabled: true,
            AutoConfirmationEnabled: false,
            DecisionRecommendationAutoExecutionEnabled: false,
            SqliteAutomationStore.MaximumAutomationsPerWorkspace,
            MinimumIntervalMinutes,
            MaximumIntervalMinutes,
            SchedulerPollSeconds,
            MaximumDueBatchSize,
            [
                "Automation chỉ gắn vào task đã tồn tại; Decision Engine recommendation không tự tạo automation.",
                "Mỗi scheduler tick chỉ tiến tối đa một task step.",
                "Background execution chỉ cho tool local READ không yêu cầu confirmation.",
                "WRITE/DELETE/EXTERNAL/SENSITIVE/COMPUTER/BROWSER/CONNECTOR/DEVELOPMENT hoặc tool cần confirmation sẽ chuyển automation sang awaiting-confirmation.",
                "Automation không bao giờ gọi Task Engine với confirmed=true.",
                "Nếu app dừng giữa lúc automation đang chạy, automation được recovery sang interrupted và bị tắt để người dùng review."
            ]);

    public AutomationListResponse GetAll()
    {
        var workspaceId =
            workspaceContext.CurrentWorkspaceId;
        return new AutomationListResponse(
            workspaceId,
            SqliteAutomationStore.MaximumAutomationsPerWorkspace,
            store.GetAll(workspaceId));
    }

    public PersonalAutomation? Get(
        Guid automationId) =>
        store.Get(
            automationId,
            workspaceContext.CurrentWorkspaceId);

    public PersonalAutomation Create(
        CreateAutomationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirmed)
        {
            throw new AutomationConfirmationRequiredException(
                "Cần xác nhận rõ ràng trước khi tạo automation.");
        }

        var workspaceId =
            workspaceContext.CurrentWorkspaceId;
        if (store.Count(workspaceId)
            >= SqliteAutomationStore.MaximumAutomationsPerWorkspace)
        {
            throw new AutomationValidationException(
                $"Workspace đã đạt giới hạn {SqliteAutomationStore.MaximumAutomationsPerWorkspace} automation.");
        }

        var task = taskEngine.Get(request.TaskId)
            ?? throw new AutomationValidationException(
                "Không tìm thấy task trong workspace hiện tại.");
        if (task.Status is PersonalTaskStatuses.Completed
            or PersonalTaskStatuses.Failed
            or PersonalTaskStatuses.Cancelled)
        {
            throw new AutomationValidationException(
                "Chỉ có thể tự động hóa task chưa kết thúc.");
        }

        var name = NormalizeName(request.Name);
        var scheduleKind =
            NormalizeScheduleKind(
                request.ScheduleKind);
        var interval =
            NormalizeInterval(
                scheduleKind,
                request.IntervalMinutes);
        var now = DateTimeOffset.UtcNow;
        var startAt =
            request.StartAt ?? now;

        if (startAt < now.AddDays(-1)
            || startAt > now.AddDays(365))
        {
            throw new AutomationValidationException(
                "Thời điểm bắt đầu automation phải nằm trong khoảng từ 24 giờ trước đến 365 ngày tới.");
        }

        var item = new PersonalAutomation(
            Guid.NewGuid(),
            workspaceId,
            name,
            task.Id,
            scheduleKind,
            startAt,
            interval,
            startAt <= now ? now : startAt,
            Enabled: true,
            AutomationStates.Scheduled,
            now,
            now,
            null,
            AutomationRunStatuses.None,
            null,
            0);

        store.Save(item);
        audit.Record(
            AuditAgents.User,
            "automation.create",
            $"automation:{item.Id:D}",
            "user-confirmed-automation",
            AuditResults.Succeeded,
            workspaceId: workspaceId);

        return item;
    }

    public PersonalAutomation? SetEnabled(
        Guid automationId,
        SetAutomationEnabledRequest request)
    {
        if (!request.Confirmed)
        {
            throw new AutomationConfirmationRequiredException(
                "Cần xác nhận rõ ràng trước khi đổi trạng thái automation.");
        }

        var workspaceId =
            workspaceContext.CurrentWorkspaceId;
        var current =
            store.Get(
                automationId,
                workspaceId);
        if (current is null)
        {
            return null;
        }

        if (current.State
            is AutomationStates.Completed
                or AutomationStates.Failed)
        {
            throw new AutomationValidationException(
                "Automation đã kết thúc; không thể thay đổi enabled state.");
        }

        if (current.State
            is AutomationStates.AwaitingConfirmation
                or AutomationStates.Interrupted)
        {
            throw new AutomationValidationException(
                "Automation cần review; hãy dùng endpoint resume sau khi đã kiểm tra task.");
        }

        if (current.State == AutomationStates.Running)
        {
            throw new AutomationValidationException(
                "Automation đang chạy; không thể đổi enabled state giữa một invocation.");
        }

        var now = DateTimeOffset.UtcNow;
        var updated = request.Enabled
            ? current with
            {
                Enabled = true,
                State = AutomationStates.Scheduled,
                NextRunAt =
                    current.NextRunAt is DateTimeOffset next
                    && next > now
                        ? next
                        : now,
                UpdatedAt = now,
                LastMessage = null
            }
            : current with
            {
                Enabled = false,
                State = AutomationStates.Paused,
                NextRunAt = null,
                UpdatedAt = now,
                LastMessage =
                    "Automation đã được người dùng tạm dừng."
            };

        store.Save(updated);
        audit.Record(
            AuditAgents.User,
            request.Enabled
                ? "automation.enable"
                : "automation.disable",
            $"automation:{automationId:D}",
            "user-confirmed-automation-state",
            AuditResults.Succeeded,
            workspaceId: workspaceId);
        return updated;
    }

    public PersonalAutomation? Resume(
        Guid automationId,
        bool confirmed)
    {
        if (!confirmed)
        {
            throw new AutomationConfirmationRequiredException(
                "Cần xác nhận sau khi đã review task trước khi tiếp tục automation.");
        }

        var workspaceId =
            workspaceContext.CurrentWorkspaceId;
        var current =
            store.Get(
                automationId,
                workspaceId);
        if (current is null)
        {
            return null;
        }

        if (current.State
            is AutomationStates.Completed
                or AutomationStates.Failed)
        {
            throw new AutomationValidationException(
                "Automation đã kết thúc; hãy tạo automation mới nếu cần.");
        }

        if (current.State
            is not (AutomationStates.AwaitingConfirmation
                or AutomationStates.Interrupted))
        {
            throw new AutomationValidationException(
                "Resume chỉ dùng cho automation đang chờ confirmation hoặc bị gián đoạn sau khi người dùng review.");
        }

        var task = taskEngine.Get(current.TaskId)
            ?? throw new AutomationValidationException(
                "Task của automation không còn tồn tại.");

        if (task.Status is PersonalTaskStatuses.Completed
            or PersonalTaskStatuses.Failed
            or PersonalTaskStatuses.Cancelled)
        {
            throw new AutomationValidationException(
                "Task đã kết thúc nên automation không thể tiếp tục.");
        }

        if (task.Status == PersonalTaskStatuses.Interrupted)
        {
            throw new AutomationValidationException(
                "Task vẫn đang interrupted; hãy resume task trước rồi mới resume automation.");
        }

        var now = DateTimeOffset.UtcNow;
        var updated = current with
        {
            Enabled = true,
            State = AutomationStates.Scheduled,
            NextRunAt = now,
            UpdatedAt = now,
            LastMessage =
                "Automation đã được người dùng tiếp tục sau khi review."
        };

        store.Save(updated);
        audit.Record(
            AuditAgents.User,
            "automation.resume",
            $"automation:{automationId:D}",
            "user-confirmed-after-review",
            AuditResults.Succeeded,
            workspaceId: workspaceId);
        return updated;
    }

    public async Task<AutomationRunResult?> RunNowAsync(
        Guid automationId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            throw new AutomationConfirmationRequiredException(
                "Cần xác nhận trước khi kích hoạt automation thủ công.");
        }

        var workspaceId =
            workspaceContext.CurrentWorkspaceId;
        var current =
            store.Get(
                automationId,
                workspaceId);
        if (current is null)
        {
            return null;
        }

        audit.Record(
            AuditAgents.User,
            "automation.run-now",
            $"automation:{automationId:D}",
            "user-confirmed-manual-trigger",
            AuditResults.Prepared,
            workspaceId: workspaceId);

        return await coordinator.RunOneAsync(
            current,
            manualTrigger: true,
            cancellationToken);
    }

    public bool Delete(
        Guid automationId,
        bool confirmed)
    {
        if (!confirmed)
        {
            throw new AutomationConfirmationRequiredException(
                "Cần xác nhận trước khi xóa automation.");
        }

        var workspaceId =
            workspaceContext.CurrentWorkspaceId;
        var deleted =
            store.Delete(
                automationId,
                workspaceId);
        if (deleted)
        {
            audit.Record(
                AuditAgents.User,
                "automation.delete",
                $"automation:{automationId:D}",
                "user-confirmed-automation-delete",
                AuditResults.Succeeded,
                workspaceId: workspaceId);
        }

        return deleted;
    }

    private static string NormalizeName(
        string? value)
    {
        var name = string.Join(
            " ",
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));

        if (name.Length is < 2
            or > MaximumNameCharacters)
        {
            throw new AutomationValidationException(
                $"Tên automation phải có từ 2 đến {MaximumNameCharacters} ký tự.");
        }

        return name;
    }

    private static string NormalizeScheduleKind(
        string? value)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();
        if (!AutomationScheduleKinds.All.Contains(
            normalized))
        {
            throw new AutomationValidationException(
                "Schedule kind chỉ được là once hoặc interval.");
        }

        return normalized;
    }

    private static int? NormalizeInterval(
        string scheduleKind,
        int? intervalMinutes)
    {
        if (scheduleKind.Equals(
            AutomationScheduleKinds.Once,
            StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var interval =
            intervalMinutes
            ?? throw new AutomationValidationException(
                "Interval automation cần intervalMinutes.");

        if (interval < MinimumIntervalMinutes
            || interval > MaximumIntervalMinutes)
        {
            throw new AutomationValidationException(
                $"Interval phải từ {MinimumIntervalMinutes} đến {MaximumIntervalMinutes} phút.");
        }

        return interval;
    }
}

public interface IAutomationCoordinator
{
    Task<AutomationRunResult> RunOneAsync(
        PersonalAutomation automation,
        bool manualTrigger,
        CancellationToken cancellationToken = default);

    Task<int> RunDueAsync(
        CancellationToken cancellationToken = default);
}

public sealed class AutomationCoordinator(
    IAutomationStore store,
    IServiceScopeFactory scopeFactory,
    IHttpContextAccessor httpContextAccessor,
    IAuditRecorder audit,
    ILogger<AutomationCoordinator> logger) : IAutomationCoordinator
{
    private readonly SemaphoreSlim _runGate =
        new(1, 1);

    public async Task<int> RunDueAsync(
        CancellationToken cancellationToken = default)
    {
        var due = store.GetDue(
            DateTimeOffset.UtcNow,
            AutomationService.MaximumDueBatchSize);
        var completed = 0;

        foreach (var item in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunOneAsync(
                item,
                manualTrigger: false,
                cancellationToken);
            completed++;
        }

        return completed;
    }

    public async Task<AutomationRunResult> RunOneAsync(
        PersonalAutomation automation,
        bool manualTrigger,
        CancellationToken cancellationToken = default)
    {
        await _runGate.WaitAsync(cancellationToken);
        try
        {
            var current = store.Get(
                automation.Id,
                automation.WorkspaceId)
                ?? throw new AutomationValidationException(
                    "Automation không còn tồn tại.");

            if (current.State
                is AutomationStates.Completed
                    or AutomationStates.Failed)
            {
                throw new AutomationValidationException(
                    "Automation đã kết thúc; không thể chạy lại cùng automation.");
            }

            if (manualTrigger
                && current.State != AutomationStates.Scheduled)
            {
                throw new AutomationValidationException(
                    "Run-now chỉ dùng cho automation đang scheduled. Automation paused phải bật lại; automation cần review phải resume trước.");
            }

            if (!manualTrigger
                && (!current.Enabled
                    || current.State
                        != AutomationStates.Scheduled))
            {
                return new AutomationRunResult(
                    current,
                    null,
                    null,
                    false,
                    false,
                    "Automation không ở trạng thái có thể chạy.");
            }

            var previousContext =
                httpContextAccessor.HttpContext;

            await using var scope =
                scopeFactory.CreateAsyncScope();
            var fakeContext =
                new DefaultHttpContext
                {
                    RequestServices =
                        scope.ServiceProvider
                };
            fakeContext.Request.Headers[
                WorkspaceEndpoints.WorkspaceHeaderName] =
                current.WorkspaceId;

            httpContextAccessor.HttpContext =
                fakeContext;

            try
            {
                var taskEngine =
                    scope.ServiceProvider
                        .GetRequiredService<ITaskEngineService>();
                var registry =
                    scope.ServiceProvider
                        .GetRequiredService<IToolRegistry>();

                var task = taskEngine.Get(
                    current.TaskId);
                if (task is null)
                {
                    return FinishFailure(
                        current,
                        "Task của automation không còn tồn tại.");
                }

                if (task.Status
                    == PersonalTaskStatuses.Completed)
                {
                    return FinishCompleted(
                        current,
                        task,
                        "Task đã hoàn tất.");
                }

                if (task.Status
                    is PersonalTaskStatuses.Failed
                        or PersonalTaskStatuses.Cancelled)
                {
                    return FinishFailure(
                        current,
                        "Task đã kết thúc ở trạng thái không thể tự động tiếp tục.",
                        task);
                }

                if (task.Status
                    == PersonalTaskStatuses.Interrupted)
                {
                    return FinishInterrupted(
                        current,
                        task,
                        "Task bị gián đoạn; cần người dùng review và resume task trước.");
                }

                var position =
                    task.CurrentStep - 1;
                if (position < 0
                    || position >= task.Steps.Count)
                {
                    return FinishFailure(
                        current,
                        "Task không còn bước hợp lệ để automation chạy.",
                        task);
                }

                var step =
                    task.Steps[position];

                if (!TaskDependenciesSatisfied(
                    task,
                    taskEngine,
                    out var taskDependencyMessage))
                {
                    return RescheduleBlocked(
                        current,
                        task,
                        taskDependencyMessage);
                }

                if (!StepDependenciesSatisfied(
                    task,
                    step,
                    out var stepDependencyMessage))
                {
                    return RescheduleBlocked(
                        current,
                        task,
                        stepDependencyMessage);
                }

                if (!registry.TryGet(
                    step.ToolName,
                    out var tool)
                    || tool is null)
                {
                    return FinishFailure(
                        current,
                        $"Tool {step.ToolName} không còn tồn tại.",
                        task);
                }

                var definition =
                    tool.Definition;
                var requiresConfirmation =
                    definition.RequiresConfirmation
                    || definition.RequiredPermissions.Any(
                        ToolPermissions.RequiresExplicitConfirmation)
                    || step.RequiresConfirmation;

                var safeBackgroundRead =
                    definition.LocalOnly
                    && definition.RequiredPermissions.Count > 0
                    && definition.RequiredPermissions.All(
                        permission =>
                            permission.Equals(
                                ToolPermissions.Read,
                                StringComparison.OrdinalIgnoreCase));

                if (requiresConfirmation
                    || !safeBackgroundRead)
                {
                    return AwaitConfirmation(
                        current,
                        task,
                        $"Bước {step.Index} ({step.ToolName}) không thuộc allowlist READ/local hoặc cần confirmation. Automation không tự xác nhận.");
                }

                var now =
                    DateTimeOffset.UtcNow;
                var running = current with
                {
                    State = AutomationStates.Running,
                    UpdatedAt = now,
                    LastRunAt = now,
                    LastMessage =
                        $"Đang chạy bước {step.Index}: {step.Title}"
                };
                store.Save(running);

                PersonalTaskStepExecutionResponse? execution;
                try
                {
                    execution =
                        await taskEngine.ExecuteNextAsync(
                            task.Id,
                            confirmed: false,
                            cancellationToken);
                }
                catch (PersonalTaskConfirmationRequiredException)
                {
                    return AwaitConfirmation(
                        running,
                        task,
                        "Task Engine yêu cầu confirmation; automation đã dừng mà không tự xác nhận.");
                }
                catch (PersonalTaskValidationException exception)
                {
                    return FinishFailure(
                        running,
                        exception.Message,
                        task);
                }

                if (execution is null)
                {
                    return FinishFailure(
                        running,
                        "Task biến mất trước khi bước automation được thực thi.",
                        task);
                }

                if (!execution.Execution.Success)
                {
                    return FinishFailure(
                        running,
                        execution.LocalSummary,
                        execution.Task,
                        execution);
                }

                var finishedTask =
                    execution.Task;
                var runCount =
                    running.RunCount + 1;

                if (finishedTask.Status
                    == PersonalTaskStatuses.Completed)
                {
                    var completedAutomation =
                        running with
                        {
                            Enabled = false,
                            State = AutomationStates.Completed,
                            NextRunAt = null,
                            UpdatedAt = DateTimeOffset.UtcNow,
                            LastRunAt = DateTimeOffset.UtcNow,
                            LastRunStatus =
                                AutomationRunStatuses.Succeeded,
                            LastMessage =
                                "Automation đã chạy một bước READ/local và task đã hoàn tất.",
                            RunCount = runCount
                        };
                    store.Save(completedAutomation);
                    RecordRunAudit(
                        completedAutomation,
                        AuditResults.Succeeded,
                        "task-completed");
                    return new AutomationRunResult(
                        completedAutomation,
                        finishedTask,
                        execution,
                        true,
                        false,
                        completedAutomation.LastMessage!);
                }

                if (running.ScheduleKind.Equals(
                    AutomationScheduleKinds.Once,
                    StringComparison.OrdinalIgnoreCase))
                {
                    var completedAutomation =
                        running with
                        {
                            Enabled = false,
                            State = AutomationStates.Completed,
                            NextRunAt = null,
                            UpdatedAt = DateTimeOffset.UtcNow,
                            LastRunAt = DateTimeOffset.UtcNow,
                            LastRunStatus =
                                AutomationRunStatuses.Succeeded,
                            LastMessage =
                                "One-time automation đã chạy đúng một task step.",
                            RunCount = runCount
                        };
                    store.Save(completedAutomation);
                    RecordRunAudit(
                        completedAutomation,
                        AuditResults.Succeeded,
                        "one-step-completed");
                    return new AutomationRunResult(
                        completedAutomation,
                        finishedTask,
                        execution,
                        true,
                        false,
                        completedAutomation.LastMessage!);
                }

                var nextRun =
                    DateTimeOffset.UtcNow
                        .AddMinutes(
                            running.IntervalMinutes
                            ?? AutomationService.MinimumIntervalMinutes);
                var scheduled =
                    running with
                    {
                        Enabled = true,
                        State = AutomationStates.Scheduled,
                        NextRunAt = nextRun,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        LastRunAt = DateTimeOffset.UtcNow,
                        LastRunStatus =
                            AutomationRunStatuses.Succeeded,
                        LastMessage =
                            "Đã chạy một task step READ/local. Automation sẽ chờ lịch kế tiếp.",
                        RunCount = runCount
                    };
                store.Save(scheduled);
                RecordRunAudit(
                    scheduled,
                    AuditResults.Succeeded,
                    "safe-read-step");
                return new AutomationRunResult(
                    scheduled,
                    finishedTask,
                    execution,
                    true,
                    false,
                    scheduled.LastMessage!);
            }
            finally
            {
                httpContextAccessor.HttpContext =
                    previousContext;
            }
        }
        finally
        {
            _runGate.Release();
        }
    }

    private AutomationRunResult AwaitConfirmation(
        PersonalAutomation current,
        PersonalTask task,
        string message)
    {
        var updated = current with
        {
            Enabled = false,
            State =
                AutomationStates.AwaitingConfirmation,
            NextRunAt = null,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastRunAt = DateTimeOffset.UtcNow,
            LastRunStatus =
                AutomationRunStatuses.AwaitingConfirmation,
            LastMessage = message
        };
        store.Save(updated);
        RecordRunAudit(
            updated,
            AuditResults.Denied,
            "confirmation-required");
        return new AutomationRunResult(
            updated,
            task,
            null,
            false,
            true,
            message);
    }

    private AutomationRunResult RescheduleBlocked(
        PersonalAutomation current,
        PersonalTask task,
        string message)
    {
        var next = DateTimeOffset.UtcNow
            .AddMinutes(
                Math.Min(
                    current.IntervalMinutes
                        ?? AutomationService.MinimumIntervalMinutes,
                    30));
        var updated = current with
        {
            Enabled = true,
            State = AutomationStates.Scheduled,
            NextRunAt = next,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastRunAt = DateTimeOffset.UtcNow,
            LastRunStatus =
                AutomationRunStatuses.Blocked,
            LastMessage = message
        };
        store.Save(updated);
        RecordRunAudit(
            updated,
            AuditResults.Blocked,
            "dependency-blocked");
        return new AutomationRunResult(
            updated,
            task,
            null,
            false,
            false,
            message);
    }

    private AutomationRunResult FinishCompleted(
        PersonalAutomation current,
        PersonalTask task,
        string message)
    {
        var updated = current with
        {
            Enabled = false,
            State = AutomationStates.Completed,
            NextRunAt = null,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastRunStatus =
                AutomationRunStatuses.Succeeded,
            LastMessage = message
        };
        store.Save(updated);
        return new AutomationRunResult(
            updated,
            task,
            null,
            false,
            false,
            message);
    }

    private AutomationRunResult FinishFailure(
        PersonalAutomation current,
        string message,
        PersonalTask? task = null,
        PersonalTaskStepExecutionResponse? execution = null)
    {
        var updated = current with
        {
            Enabled = false,
            State = AutomationStates.Failed,
            NextRunAt = null,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastRunAt = DateTimeOffset.UtcNow,
            LastRunStatus =
                AutomationRunStatuses.Failed,
            LastMessage = message,
            RunCount = current.RunCount
                + (execution is null ? 0 : 1)
        };
        store.Save(updated);
        RecordRunAudit(
            updated,
            AuditResults.Failed,
            "automation-failed");
        return new AutomationRunResult(
            updated,
            task,
            execution,
            execution is not null,
            false,
            message);
    }

    private AutomationRunResult FinishInterrupted(
        PersonalAutomation current,
        PersonalTask task,
        string message)
    {
        var updated = current with
        {
            Enabled = false,
            State = AutomationStates.Interrupted,
            NextRunAt = null,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastRunAt = DateTimeOffset.UtcNow,
            LastRunStatus =
                AutomationRunStatuses.Interrupted,
            LastMessage = message
        };
        store.Save(updated);
        RecordRunAudit(
            updated,
            AuditResults.Interrupted,
            "task-interrupted");
        return new AutomationRunResult(
            updated,
            task,
            null,
            false,
            false,
            message);
    }

    private static bool TaskDependenciesSatisfied(
        PersonalTask task,
        ITaskEngineService taskEngine,
        out string message)
    {
        var dependencies =
            task.DependsOnTaskIds ?? [];
        foreach (var dependencyId in dependencies)
        {
            var dependency =
                taskEngine.Get(dependencyId);
            if (dependency is null)
            {
                message =
                    $"Task dependency {dependencyId:D} không còn tồn tại.";
                return false;
            }

            if (dependency.Status
                != PersonalTaskStatuses.Completed)
            {
                message =
                    $"Automation đang chờ task dependency '{dependency.Goal}' hoàn tất.";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private static bool StepDependenciesSatisfied(
        PersonalTask task,
        PersonalTaskStep step,
        out string message)
    {
        foreach (var dependency
            in step.DependsOn ?? [])
        {
            var required =
                task.Steps.FirstOrDefault(
                    candidate =>
                        candidate.Index
                        == dependency);
            if (required is null
                || required.Status
                    != PersonalTaskStepStatuses.Completed)
            {
                message =
                    $"Automation đang chờ step dependency {dependency} hoàn tất.";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private void RecordRunAudit(
        PersonalAutomation automation,
        string result,
        string reason)
    {
        try
        {
            audit.Record(
                AuditAgents.Automation,
                "automation.run",
                $"automation:{automation.Id:D}",
                reason,
                result,
                workspaceId:
                    automation.WorkspaceId);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Không thể ghi audit cho automation {AutomationId}.",
                automation.Id);
        }
    }
}

public sealed class AutomationBackgroundService(
    IAutomationCoordinator coordinator,
    ILogger<AutomationBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await Task.Delay(
            TimeSpan.FromSeconds(5),
            stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await coordinator.RunDueAsync(
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Automation scheduler tick failed.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(
                    AutomationService.SchedulerPollSeconds),
                stoppingToken);
        }
    }
}
