namespace PersonalAI.Web.Models;

public sealed record ChatMessage(string Role, string Content);

public sealed record ChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    bool UseKnowledge = true,
    string KnowledgeMode = "normal",
    bool UseMemory = true,
    bool UseTools = false);

public sealed record ChatSource(
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    int? PageNumber = null,
    string? Heading = null,
    string? Section = null);

public sealed record ChatResponse(
    string Message,
    string Model,
    string Provider,
    IReadOnlyList<ChatSource> Sources,
    ToolCallProposal? ToolProposal = null);

public sealed record ApiError(string Error);
