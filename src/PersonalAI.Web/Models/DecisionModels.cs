namespace PersonalAI.Web.Models;

public sealed record DecisionAnalyzeRequest(
    string Question,
    IReadOnlyList<string> Options,
    IReadOnlyList<string>? Criteria = null,
    string? Constraints = null,
    bool UseKnowledge = true,
    bool UseMemory = true,
    bool UseTaskContext = true,
    bool UseLifeContext = true);

public sealed record DecisionPreviewResponse(
    Guid DecisionId,
    string WorkspaceId,
    IReadOnlyList<string> Options,
    IReadOnlyList<string> Criteria,
    ContextSelectionReport Context,
    IReadOnlyList<ChatSource> Sources,
    bool RequiresUserDecision,
    bool AutoActionEnabled,
    bool ToolExecutionEnabled);

public sealed record DecisionAnalysisResponse(
    Guid DecisionId,
    string Analysis,
    string Provider,
    string Model,
    IReadOnlyList<ChatSource> Sources,
    ContextSelectionReport Context,
    bool RequiresUserDecision,
    bool AutoActionEnabled,
    bool ToolExecutionEnabled,
    bool Persisted);

public sealed record DecisionEngineStatusResponse(
    string Version,
    bool Supported,
    bool RequiresUserDecision,
    bool AutoActionEnabled,
    bool ToolExecutionEnabled,
    bool PersistsAnalyses,
    bool ContextGroundingEnabled,
    int MaximumQuestionCharacters,
    int MaximumOptions,
    int MaximumCriteria,
    int MaximumOptionCharacters,
    int MaximumCriterionCharacters,
    int MaximumConstraintsCharacters,
    IReadOnlyList<string> ContextKinds,
    IReadOnlyList<string> Limitations);
