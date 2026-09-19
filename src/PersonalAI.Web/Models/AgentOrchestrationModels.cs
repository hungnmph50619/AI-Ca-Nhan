namespace PersonalAI.Web.Models;

public static class AgentWorkflowStatuses
{
    public const string Completed = "completed";
    public const string Stopped = "stopped";
    public const string TimedOut = "timed-out";
}

public sealed record AgentWorkflowStepRequest(
    string? AgentId,
    string? Goal,
    bool IncludePreviousOutput = false);

public sealed record AgentWorkflowRequest(
    IReadOnlyList<AgentWorkflowStepRequest>? Steps,
    bool ConfirmSelectedWorkflow = false,
    bool UseKnowledge = false,
    bool UseMemory = false,
    bool UseTaskContext = false,
    bool UseLifeContext = false);

public sealed record AgentWorkflowStepResult(
    int Step,
    string AgentId,
    bool PreviousOutputTransferred,
    AgentExecutionResponse Result);

public sealed record AgentWorkflowResponse(
    Guid WorkflowId,
    string WorkspaceId,
    string Status,
    IReadOnlyList<AgentWorkflowStepResult> CompletedSteps,
    int RequestedSteps,
    int? FailedStep,
    string? Error,
    bool ExecutedTools,
    bool AutomaticallySelectedAgents,
    bool PersistedWorkflow,
    bool ParallelExecution,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? StopReason = null);

public sealed record AgentOrchestrationStatusResponse(
    string Version,
    int MinimumSteps,
    int MaximumSteps,
    int MaximumTransferredCharacters,
    bool ExplicitWorkflowConfirmationRequired,
    bool ExplicitHandoffRequired,
    bool SequentialExecutionEnabled,
    bool AutomaticAgentSelectionEnabled,
    bool ParallelAgentExecutionEnabled,
    bool SharedTaskQueueEnabled,
    bool ToolExecutionEnabled,
    bool PersistsWorkflows,
    bool AutonomousLoopEnabled,
    bool SelfTestPassed,
    string NextStage,
    int MaximumWorkflowSeconds = 0,
    int MaximumStepSeconds = 0,
    bool StopsOnTimeout = false,
    bool SensitiveHandoffScreeningEnabled = false,
    bool HandoffGuardSelfTestPassed = false);
