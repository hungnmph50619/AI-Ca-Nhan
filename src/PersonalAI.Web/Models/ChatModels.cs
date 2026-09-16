namespace PersonalAI.Web.Models;

public sealed record ChatMessage(string Role, string Content);

public sealed record ChatRequest(IReadOnlyList<ChatMessage> Messages);

public sealed record ChatSource(
    Guid DocumentId,
    string FileName,
    int ChunkIndex);

public sealed record ChatResponse(
    string Message,
    string Model,
    string Provider,
    IReadOnlyList<ChatSource> Sources);

public sealed record ApiError(string Error);
