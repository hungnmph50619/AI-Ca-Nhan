namespace PersonalAI.Web.Models;

public sealed record PersonalMemory(
    Guid Id,
    string Kind,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreatePersonalMemoryRequest(
    string? Kind,
    string? Content);
