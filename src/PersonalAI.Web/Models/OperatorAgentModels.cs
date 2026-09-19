namespace PersonalAI.Web.Models;

public static class OperatorPlanStatuses
{
    public const string Prepared = "prepared";
}

public static class OperatorActionChannels
{
    public const string Browser = "browser";
    public const string Computer = "computer";
    public const string Connector = "connector";
    public const string Manual = "manual";

    public static readonly IReadOnlyList<string> All =
        [Browser, Computer, Connector, Manual];
}

public static class OperatorRiskLevels
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    public static readonly IReadOnlyList<string> All =
        [Low, Medium, High];
}

public sealed record OperatorActionStep(
    int Step,
    string Title,
    string Description,
    string Channel,
    IReadOnlyList<int> DependsOn,
    string RiskLevel,
    bool RequiresUserConfirmation,
    string ExpectedOutcome,
    string VerificationStep);

public sealed record OperatorActionPlan(
    string Goal,
    string Status,
    string Summary,
    IReadOnlyList<OperatorActionStep> Steps,
    IReadOnlyList<string> Preconditions,
    IReadOnlyList<string> OpenQuestions,
    IReadOnlyList<string> Limitations,
    bool ExecutedActions,
    bool UsedTools,
    bool SentMessages,
    bool ChangedComputerState,
    bool PersistedAutomatically,
    bool DispatchedAgents,
    DateTimeOffset CreatedAt);

public sealed record OperatorAgentStatusResponse(
    string Version,
    string AgentId,
    int MinimumSteps,
    int MaximumSteps,
    IReadOnlyList<string> AllowedChannels,
    IReadOnlyList<string> AllowedRiskLevels,
    bool StructuredOutput,
    bool ParserSelfTestPassed,
    bool ExecutedActions,
    bool UsedTools,
    bool ChangedComputerState,
    bool PersistsPlans,
    bool DispatchesAgents,
    bool RequiresExplicitInvocation,
    string NextStage);
