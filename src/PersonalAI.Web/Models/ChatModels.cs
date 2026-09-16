namespace PersonalAI.Web.Models;

public sealed record ChatMessage(string Role, string Content);

public sealed record ChatRequest(IReadOnlyList<ChatMessage> Messages);

public sealed record ChatResponse(string Message, string Model);

public sealed record ApiError(string Error);
