namespace PersonalAI.Web.Models;

public sealed record KnowledgeDocumentResponse(
    Guid Id,
    string FileName,
    string FileType,
    long FileSize,
    int CharacterCount,
    int ChunkCount,
    string Status,
    DateTimeOffset CreatedAt,
    int? PageCount = null);

public sealed record KnowledgeSearchResult(
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    string Content);

public sealed record KnowledgeSearchResponse(
    string Query,
    int ResultCount,
    IReadOnlyList<KnowledgeSearchResult> Results);
