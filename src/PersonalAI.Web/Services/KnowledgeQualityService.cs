using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IKnowledgeQualityService
{
    Task<KnowledgeQualityStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task<KnowledgeEvaluationResponse> EvaluateAsync(
        KnowledgeEvaluationRequest request,
        CancellationToken cancellationToken = default);

    Task<KnowledgeRepairResponse> RepairAsync(
        CancellationToken cancellationToken = default);
}

public sealed class KnowledgeQualityService(
    IKnowledgeDocumentStore knowledgeStore,
    IKnowledgeEmbeddingIndex embeddingIndex,
    IKnowledgeHybridSearchService hybridSearch,
    IKnowledgeDocumentManagementService managementService,
    ILogger<KnowledgeQualityService> logger) : IKnowledgeQualityService
{
    private const int MaximumEvaluationCases = 50;
    private readonly SemaphoreSlim _repairLock = new(1, 1);
    private readonly string _knowledgeDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalAI",
        "Knowledge");

    private string DatabasePath => Path.Combine(_knowledgeDirectory, "personal-ai.db");
    private string FilesDirectory => Path.Combine(_knowledgeDirectory, "files");

    public async Task<KnowledgeQualityStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        await knowledgeStore.InitializeAsync(cancellationToken);
        var embeddingStatus = await embeddingIndex.GetStatusAsync(cancellationToken);
        var issues = new List<KnowledgeQualityIssue>();
        var databaseHealthy = true;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using (var quickCheck = connection.CreateCommand())
        {
            quickCheck.CommandText = "PRAGMA quick_check;";
            var result = (string?)await quickCheck.ExecuteScalarAsync(cancellationToken);
            databaseHealthy = string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }

        if (databaseHealthy)
        {
            await using var foreignKeyCheck = connection.CreateCommand();
            foreignKeyCheck.CommandText = "PRAGMA foreign_key_check;";
            await using var foreignKeyReader = await foreignKeyCheck.ExecuteReaderAsync(cancellationToken);
            databaseHealthy = !await foreignKeyReader.ReadAsync(cancellationToken);
        }

        var documents = new List<DocumentSnapshot>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT d.Id, d.FileName, d.StoredFileName, d.Sha256, d.Status,
                       d.ExtractedText,
                       COUNT(DISTINCT c.Id) AS ChunkCount,
                       COUNT(DISTINCT c.ChunkIndex) AS DistinctChunkIndexes,
                       MIN(c.ChunkIndex) AS MinChunkIndex,
                       MAX(c.ChunkIndex) AS MaxChunkIndex,
                       COUNT(DISTINCT CASE
                           WHEN e.ModelId = $modelId AND e.Dimensions = $dimensions
                           THEN e.ChunkId END) AS IndexedChunkCount
                FROM Documents d
                LEFT JOIN DocumentChunks c ON c.DocumentId = d.Id
                LEFT JOIN ChunkEmbeddings e ON e.ChunkId = c.Id
                GROUP BY d.Id, d.FileName, d.StoredFileName, d.Sha256, d.Status, d.ExtractedText
                ORDER BY d.CreatedAt DESC;
                """;
            command.Parameters.AddWithValue("$modelId", embeddingStatus.EmbeddingModel);
            command.Parameters.AddWithValue("$dimensions", embeddingStatus.Dimensions);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                documents.Add(new DocumentSnapshot(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    checked((int)Math.Min(reader.GetInt64(6), int.MaxValue)),
                    checked((int)Math.Min(reader.GetInt64(7), int.MaxValue)),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    checked((int)Math.Min(reader.GetInt64(10), int.MaxValue))));
            }
        }

        var contentGroups = documents
            .Where(document => !string.IsNullOrWhiteSpace(document.ExtractedText))
            .GroupBy(document => CalculateNormalizedContentHash(document.ExtractedText), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToArray();

        var duplicateIds = contentGroups
            .SelectMany(group => group.Skip(1).Select(document => document.Id))
            .ToHashSet();

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileNameIsSafe = string.Equals(
                Path.GetFileName(document.StoredFileName),
                document.StoredFileName,
                StringComparison.Ordinal);
            var originalPath = fileNameIsSafe
                ? Path.Combine(FilesDirectory, document.StoredFileName)
                : string.Empty;

            if (!fileNameIsSafe || !File.Exists(originalPath))
            {
                issues.Add(Issue(document, "missing-original", "error",
                    "Không tìm thấy tệp gốc trên máy hoặc tên tệp lưu nội bộ không hợp lệ."));
            }
            else
            {
                var actualHash = await CalculateFileSha256Async(originalPath, cancellationToken);
                if (!string.Equals(actualHash, document.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(Issue(document, "hash-mismatch", "error",
                        "Mã kiểm tra của tệp gốc không còn khớp thông tin mô tả đã lưu; không nên tự động lập lại chỉ mục."));
                }
            }

            if (document.ChunkCount == 0)
            {
                issues.Add(Issue(document, "missing-chunks", "warning",
                    "Tài liệu chưa có đoạn dữ liệu để tìm kiếm."));
            }
            else if (document.DistinctChunkIndexes != document.ChunkCount
                     || document.MinChunkIndex != 1
                     || document.MaxChunkIndex != document.ChunkCount)
            {
                issues.Add(Issue(document, "non-contiguous-chunks", "warning",
                    "Thứ tự đoạn dữ liệu không liên tục hoặc có số thứ tự bị trùng."));
            }

            if (document.IndexedChunkCount < document.ChunkCount)
            {
                issues.Add(Issue(document, "missing-embeddings", "warning",
                    $"Còn {document.ChunkCount - document.IndexedChunkCount} đoạn dữ liệu chưa có véc-tơ hiện hành."));
            }

            if (!string.Equals(document.Status, "ready", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(Issue(document, "document-status", "warning",
                    $"Trạng thái tài liệu hiện là '{document.Status}'."));
            }

            if (duplicateIds.Contains(document.Id))
            {
                issues.Add(Issue(document, "duplicate-content", "warning",
                    "Nội dung chuẩn hóa trùng với một tài liệu khác trong kho."));
            }
        }

        var issueDocumentIds = issues
            .Where(issue => issue.DocumentId != Guid.Empty)
            .Select(issue => issue.DocumentId)
            .ToHashSet();
        var errorDocumentIds = issues
            .Where(issue => issue.Severity == "error")
            .Select(issue => issue.DocumentId)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        var warningDocumentIds = issues
            .Where(issue => issue.Severity == "warning" && !errorDocumentIds.Contains(issue.DocumentId))
            .Select(issue => issue.DocumentId)
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        var overallStatus = !databaseHealthy || errorDocumentIds.Count > 0
            ? "error"
            : warningDocumentIds.Count > 0
                ? "degraded"
                : "healthy";

        return new KnowledgeQualityStatus(
            overallStatus,
            databaseHealthy,
            documents.Count,
            Math.Max(0, documents.Count - issueDocumentIds.Count),
            warningDocumentIds.Count,
            errorDocumentIds.Count,
            issues.Count(issue => issue.Code == "missing-original"),
            issues.Count(issue => issue.Code == "hash-mismatch"),
            issues.Count(issue => issue.Code is "missing-chunks" or "non-contiguous-chunks"),
            issues.Count(issue => issue.Code == "missing-embeddings"),
            issues.Count(issue => issue.Code == "duplicate-content"),
            embeddingStatus.EmbeddingModel,
            embeddingStatus.Dimensions,
            DateTimeOffset.UtcNow,
            issues);
    }

    public async Task<KnowledgeEvaluationResponse> EvaluateAsync(
        KnowledgeEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        var cases = request.Cases ?? [];
        if (cases.Count == 0)
        {
            throw new KnowledgeDocumentValidationException(
                "Hãy cung cấp ít nhất một trường hợp đánh giá chất lượng tìm kiếm.");
        }

        if (cases.Count > MaximumEvaluationCases)
        {
            throw new KnowledgeDocumentValidationException(
                $"Mỗi lần đánh giá tối đa {MaximumEvaluationCases} trường hợp.");
        }

        var limit = Math.Clamp(request.Limit ?? 5, 1, 10);
        var results = new List<KnowledgeEvaluationCaseResult>(cases.Count);
        var reciprocalRankSum = 0d;
        var hitCount = 0;
        var contentMatchCount = 0;

        foreach (var testCase in cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = testCase.Query?.Trim() ?? string.Empty;
            var expectedFileName = Path.GetFileName(testCase.ExpectedFileName?.Trim() ?? string.Empty);
            if (query.Length < 2 || expectedFileName.Length == 0)
            {
                throw new KnowledgeDocumentValidationException(
                    "Mỗi trường hợp đánh giá cần nội dung tìm kiếm từ 2 ký tự và tên tệp kỳ vọng hợp lệ.");
            }

            var response = await hybridSearch.SearchAsync(query, limit, cancellationToken);
            var rank = response.Results
                .Select((item, index) => new { item, rank = index + 1 })
                .FirstOrDefault(pair => string.Equals(
                    pair.item.FileName,
                    expectedFileName,
                    StringComparison.OrdinalIgnoreCase));

            var hit = rank is not null;
            var expectedContains = string.IsNullOrWhiteSpace(testCase.ExpectedContains)
                ? null
                : testCase.ExpectedContains.Trim();
            var contentMatched = hit && (expectedContains is null
                || rank!.item.Content.Contains(expectedContains, StringComparison.OrdinalIgnoreCase));

            if (hit)
            {
                hitCount++;
                reciprocalRankSum += 1d / rank!.rank;
            }
            if (contentMatched)
            {
                contentMatchCount++;
            }

            var top = response.Results.FirstOrDefault();
            results.Add(new KnowledgeEvaluationCaseResult(
                query,
                expectedFileName,
                expectedContains,
                hit,
                rank?.rank,
                contentMatched,
                top?.FileName,
                top?.HybridScore));
        }

        var count = cases.Count;
        return new KnowledgeEvaluationResponse(
            count,
            results.Count(result => result.Hit && result.ContentMatched),
            Math.Round((double)hitCount / count, 4),
            Math.Round(reciprocalRankSum / count, 4),
            Math.Round((double)contentMatchCount / count, 4),
            "keyword+local-vector+deterministic-rerank-v1",
            results);
    }

    public async Task<KnowledgeRepairResponse> RepairAsync(
        CancellationToken cancellationToken = default)
    {
        await _repairLock.WaitAsync(cancellationToken);
        try
        {
            var before = await GetStatusAsync(cancellationToken);
            if (!before.DatabaseHealthy)
            {
                throw new KnowledgeDocumentValidationException(
                    "Cơ sở dữ liệu SQLite không vượt qua kiểm tra tính toàn vẹn. Không chạy sửa tự động để tránh làm hỏng dữ liệu thêm.");
            }

            var grouped = before.Issues
                .Where(issue => issue.DocumentId != Guid.Empty)
                .GroupBy(issue => issue.DocumentId)
                .ToArray();

            var attempted = 0;
            var reindexed = 0;
            var repairedEmbeddings = 0;
            var skipped = 0;
            var failed = 0;

            foreach (var group in grouped)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var codes = group.Select(issue => issue.Code).ToHashSet(StringComparer.Ordinal);
                if (codes.Contains("missing-original")
                    || codes.Contains("hash-mismatch")
                    || codes.SetEquals(["duplicate-content"]))
                {
                    skipped++;
                    continue;
                }

                var needsReindex = codes.Contains("missing-chunks")
                    || codes.Contains("non-contiguous-chunks")
                    || codes.Contains("document-status");
                var needsEmbeddingRepair = codes.Contains("missing-embeddings");
                if (!needsReindex && !needsEmbeddingRepair)
                {
                    skipped++;
                    continue;
                }

                attempted++;
                try
                {
                    if (needsReindex)
                    {
                        var result = await managementService.ReindexAsync(group.Key, cancellationToken);
                        if (result is null)
                        {
                            failed++;
                        }
                        else
                        {
                            reindexed++;
                        }
                    }
                    else
                    {
                        await embeddingIndex.IndexDocumentAsync(group.Key, cancellationToken);
                        repairedEmbeddings++;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failed++;
                    logger.LogWarning(exception,
                        "RAG quality repair failed for document {DocumentId}.", group.Key);
                }
            }

            var after = await GetStatusAsync(cancellationToken);
            return new KnowledgeRepairResponse(
                attempted,
                reindexed,
                repairedEmbeddings,
                skipped,
                failed,
                after);
        }
        finally
        {
            _repairLock.Release();
        }
    }

    private SqliteConnection CreateConnection()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true
        }.ToString();
        return new SqliteConnection(connectionString);
    }

    private static KnowledgeQualityIssue Issue(
        DocumentSnapshot document,
        string code,
        string severity,
        string message) =>
        new(document.Id, document.FileName, code, severity, message);

    private static async Task<string> CalculateFileSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string CalculateNormalizedContentHash(string text)
    {
        var normalized = Regex.Replace(
                (text ?? string.Empty).Normalize(NormalizationForm.FormKC).ToLowerInvariant(),
                @"\s+",
                " ")
            .Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private sealed record DocumentSnapshot(
        Guid Id,
        string FileName,
        string StoredFileName,
        string Sha256,
        string Status,
        string ExtractedText,
        int ChunkCount,
        int DistinctChunkIndexes,
        int? MinChunkIndex,
        int? MaxChunkIndex,
        int IndexedChunkCount);
}
