namespace PersonalAI.Web.Models;

public sealed record ChatMessage(string Role, string Content);

public sealed record ChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    bool UseKnowledge = true,
    string KnowledgeMode = "normal",
    bool UseMemory = true);

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
    IReadOnlyList<ChatSource> Sources);

public sealed record ApiError(string Error);
