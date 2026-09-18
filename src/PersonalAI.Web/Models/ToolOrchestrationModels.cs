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
    DateTimeOffset ExpiresAt);

public sealed record ExecuteToolProposalRequest(
    Guid ProposalId,
    bool Confirmed = false);

public sealed record ToolProposalExecutionResponse(
    ToolCallProposal Proposal,
    ToolExecutionResponse Execution,
    string LocalSummary,
    bool CanAiSynthesize,
    DateTimeOffset? SynthesisExpiresAt = null);

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

internal sealed record ToolPlannerDecision(
    string Action,
    string? ToolName,
    JsonElement? Arguments,
    string? Reason,
    string? Message);
