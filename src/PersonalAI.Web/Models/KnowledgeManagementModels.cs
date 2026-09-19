namespace PersonalAI.Web.Models;

public sealed record RenameKnowledgeDocumentRequest(string? FileName);

public sealed record BulkDeleteKnowledgeDocumentsRequest(
    IReadOnlyList<Guid>? DocumentIds);

public sealed record KnowledgeDocumentManagementItem(
    Guid Id,
    string FileName,
    string FileType,
    long FileSize,
    int CharacterCount,
    int ChunkCount,
    int IndexedChunkCount,
    int MissingEmbeddingCount,
    string Status,
    string IndexStatus,
    DateTimeOffset CreatedAt,
    int? PageCount,
    string Sha256);

public sealed record KnowledgeDocumentManagementResponse(
    int Total,
    int Ready,
    int NeedsIndexing,
    string EmbeddingModel,
    int Dimensions,
    IReadOnlyList<KnowledgeDocumentManagementItem> Documents);

public sealed record BulkDeleteKnowledgeDocumentsResponse(
    int Requested,
    int Deleted,
    int NotFound);

public sealed record KnowledgeDocumentMetadataExport(
    int Version,
    DateTimeOffset ExportedAt,
    string EmbeddingModel,
    int Dimensions,
    IReadOnlyList<KnowledgeDocumentManagementItem> Documents);
