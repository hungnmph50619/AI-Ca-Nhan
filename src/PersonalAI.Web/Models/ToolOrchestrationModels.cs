using System.Text.Json;

namespace PersonalAI.Web.Models;

public sealed record ToolProposalDraft(
    string ToolName,
    JsonElement Arguments,
    string? Reason = null,
    string? AssistantMessage = null);

public sealed record ToolCallProposal(
    Guid ProposalId,
    string ToolName,
    JsonElement Arguments,
    string Reason,
    string AssistantMessage,
    IReadOnlyList<string> RequiredPermissions,
    bool RequiresConfirmation,
    DateTimeOffset ExpiresAt,
    string PlanningMode = "manual",
    string? PlanningProvider = null,
    string? PlanningModel = null);

public sealed record ExecuteToolProposalRequest(
    Guid ProposalId,
    bool Confirmed = false);

public sealed record ToolProposalExecutionResponse(
    ToolCallProposal Proposal,
    ToolExecutionResponse Execution,
    string LocalSummary,
    bool CanAiSynthesize,
    DateTimeOffset? SynthesisExpiresAt = null,
    bool CanNativeContinue = false,
    DateTimeOffset? NativeContinueExpiresAt = null);

public sealed record ToolResultSynthesisRequest(
    Guid InvocationId,
    bool ConfirmedExternal = false);

public sealed record ToolResultSynthesisResponse(
    Guid InvocationId,
    string Mode,
    string Message,
    string? Provider,
    string? Model,
    bool OutputTruncated,
    DateTimeOffset CreatedAt);


public sealed record ProviderFunctionDefinition(
    string Name,
    string Description,
    JsonElement Parameters);

public sealed record ProviderFunctionCallDecision(
    string Name,
    JsonElement Arguments,
    ProviderFunctionCallContext Context);

public sealed record ProviderFunctionCallContext(
    string Provider,
    string Model,
    string FunctionName,
    string? ResponseId,
    string? CallId,
    JsonElement Arguments,
    IReadOnlyList<ChatMessage> Messages);

public sealed record ToolNativeContinuationRequest(
    Guid InvocationId,
    bool ConfirmedExternal = false);

public sealed record ToolNativeContinuationResponse(
    Guid InvocationId,
    string Mode,
    string Message,
    string Provider,
    string Model,
    bool OutputTruncated,
    DateTimeOffset CreatedAt);
