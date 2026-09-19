namespace PersonalAI.Web.Models;

public static class PlannerPlanStatuses
{
    public const string Prepared = "prepared";
}

public static class PlannerSuggestedRoles
{
    public static readonly IReadOnlyList<string> All =
    [
        "general-assistant",
        "research",
        "developer",
        "office",
        "operator",
        "reviewer",
        "security",
        "user"
    ];
}

public sealed record PlannerPlanStep(
    int Step,
    string Title,
    string Description,
    IReadOnlyList<int> DependsOn,
    string SuggestedRole,
    IReadOnlyList<string> RequiredCapabilities,
    bool RequiresUserInput,
    string ExpectedOutcome);

public sealed record PlannerPlan(
    Guid PlanId,
    string Goal,
    string Summary,
    string Status,
    IReadOnlyList<PlannerPlanStep> Steps,
    IReadOnlyList<string> OpenQuestions,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Risks,
    DateTimeOffset CreatedAt,
    bool CreatesTasks,
    bool DispatchesAgents,
    bool PersistsAutomatically);

public sealed record PlannerAgentStatusResponse(
    string Version,
    string AgentId,
    int MinimumSteps,
    int MaximumSteps,
    int MaximumOpenQuestions,
    int MaximumAssumptions,
    int MaximumRisks,
    IReadOnlyList<string> SuggestedRoles,
    bool StructuredOutput,
    bool CreatesTasks,
    bool DispatchesAgents,
    bool PersistsPlans,
    bool ToolExecutionEnabled,
    bool RequiresExplicitInvocation,
    string NextStage);
