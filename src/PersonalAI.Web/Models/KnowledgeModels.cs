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
    string Content,
    int? PageNumber = null,
    string? Heading = null,
    string? Section = null,
    int? TokenEstimate = null,
    double? SimilarityScore = null);

public sealed record KnowledgeSearchResponse(
    string Query,
    int ResultCount,
    IReadOnlyList<KnowledgeSearchResult> Results);

public sealed record KnowledgeSemanticSearchResponse(
    string Query,
    int ResultCount,
    string EmbeddingModel,
    int Dimensions,
    IReadOnlyList<KnowledgeSearchResult> Results);

public sealed record KnowledgeEmbeddingStatus(
    string EmbeddingModel,
    int Dimensions,
    int TotalChunks,
    int IndexedChunks,
    int MissingChunks,
    bool Local);
