namespace PersonalAI.Web.Models;

public sealed record KnowledgeDocumentResponse(
    Guid Id,
    string FileName,
    string FileType,
    long FileSize,
    int CharacterCount,
    string Status,
    DateTimeOffset CreatedAt);
