namespace PersonalAI.Web.Models;

public sealed record KnowledgeQualityIssue(
    Guid DocumentId,
    string FileName,
    string Code,
    string Severity,
    string Message);

public sealed record KnowledgeQualityStatus(
    string Status,
    bool DatabaseHealthy,
    int TotalDocuments,
    int HealthyDocuments,
    int WarningDocuments,
    int ErrorDocuments,
    int MissingOriginalFiles,
    int HashMismatches,
    int MissingChunks,
    int MissingEmbeddings,
    int DuplicateContentDocuments,
    string EmbeddingModel,
    int Dimensions,
    DateTimeOffset CheckedAt,
    IReadOnlyList<KnowledgeQualityIssue> Issues);

public sealed record KnowledgeEvaluationCase(
    string Query,
    string ExpectedFileName,
    string? ExpectedContains = null);

public sealed record KnowledgeEvaluationRequest(
    IReadOnlyList<KnowledgeEvaluationCase>? Cases,
    int? Limit = 5);

public sealed record KnowledgeEvaluationCaseResult(
    string Query,
    string ExpectedFileName,
    string? ExpectedContains,
    bool Hit,
    int? Rank,
    bool ContentMatched,
    string? TopFileName,
    double? TopHybridScore);

public sealed record KnowledgeEvaluationResponse(
    int CaseCount,
    int Passed,
    double HitRate,
    double MeanReciprocalRank,
    double ContentMatchRate,
    string Strategy,
    IReadOnlyList<KnowledgeEvaluationCaseResult> Results);

public sealed record KnowledgeRepairResponse(
    int Attempted,
    int ReindexedDocuments,
    int RepairedEmbeddingDocuments,
    int SkippedDocuments,
    int FailedDocuments,
    KnowledgeQualityStatus StatusAfterRepair);
