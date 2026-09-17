using System.Globalization;
using Microsoft.Data.Sqlite;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IKnowledgeDocumentManagementService
{
    Task<KnowledgeDocumentManagementResponse> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<KnowledgeDocumentManagementItem?> RenameAsync(
        Guid documentId,
        string? requestedFileName,
        CancellationToken cancellationToken = default);

    Task<KnowledgeDocumentManagementItem?> ReindexAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<BulkDeleteKnowledgeDocumentsResponse> DeleteManyAsync(
        IReadOnlyList<Guid>? documentIds,
        CancellationToken cancellationToken = default);

    Task<KnowledgeDocumentMetadataExport> ExportAsync(
        CancellationToken cancellationToken = default);
}

public sealed class KnowledgeDocumentManagementService(
    IKnowledgeDocumentStore knowledgeStore,
    IKnowledgeEmbeddingIndex embeddingIndex,
    KnowledgeDocumentExtractor extractor,
    ILogger<KnowledgeDocumentManagementService> logger) : IKnowledgeDocumentManagementService
{
    private readonly KnowledgeDocumentChunker _chunker = new();
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private readonly string _knowledgeDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalAI",
        "Knowledge");

    private string DatabasePath => Path.Combine(_knowledgeDirectory, "personal-ai.db");
    private string FilesDirectory => Path.Combine(_knowledgeDirectory, "files");

    public async Task<KnowledgeDocumentManagementResponse> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await knowledgeStore.InitializeAsync(cancellationToken);
        var embeddingStatus = await embeddingIndex.GetStatusAsync(cancellationToken);
        var documents = await ReadDocumentsAsync(
            embeddingStatus.EmbeddingModel,
            embeddingStatus.Dimensions,
            null,
            cancellationToken);

        return new KnowledgeDocumentManagementResponse(
            documents.Count,
            documents.Count(item => item.IndexStatus == "ready"),
            documents.Count(item => item.IndexStatus == "needs-indexing"),
            embeddingStatus.EmbeddingModel,
            embeddingStatus.Dimensions,
            documents);
    }

    public async Task<KnowledgeDocumentManagementItem?> RenameAsync(
        Guid documentId,
        string? requestedFileName,
        CancellationToken cancellationToken = default)
    {
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            await knowledgeStore.InitializeAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            string? currentFileName;
            await using (var select = connection.CreateCommand())
            {
                select.CommandText = "SELECT FileName FROM Documents WHERE Id = $id LIMIT 1;";
                select.Parameters.AddWithValue("$id", documentId.ToString("D"));
                currentFileName = (string?)await select.ExecuteScalarAsync(cancellationToken);
            }

            if (currentFileName is null)
            {
                return null;
            }

            var normalizedFileName = NormalizeRenamedFileName(requestedFileName, currentFileName);
            if (string.Equals(normalizedFileName, currentFileName, StringComparison.Ordinal))
            {
                return await GetOneAsync(documentId, cancellationToken);
            }

            await using (var duplicateCheck = connection.CreateCommand())
            {
                duplicateCheck.CommandText = """
                    SELECT 1
                    FROM Documents
                    WHERE Id <> $id AND FileName = $fileName COLLATE NOCASE
                    LIMIT 1;
                    """;
                duplicateCheck.Parameters.AddWithValue("$id", documentId.ToString("D"));
                duplicateCheck.Parameters.AddWithValue("$fileName", normalizedFileName);
                if (await duplicateCheck.ExecuteScalarAsync(cancellationToken) is not null)
                {
                    throw new KnowledgeDocumentValidationException(
                        "Đã có tài liệu khác dùng tên này. Hãy chọn tên khác.");
                }
            }

            await using (var update = connection.CreateCommand())
            {
                update.CommandText = "UPDATE Documents SET FileName = $fileName WHERE Id = $id;";
                update.Parameters.AddWithValue("$fileName", normalizedFileName);
                update.Parameters.AddWithValue("$id", documentId.ToString("D"));
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            return await GetOneAsync(documentId, cancellationToken);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<KnowledgeDocumentManagementItem?> ReindexAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            await knowledgeStore.InitializeAsync(cancellationToken);
            var stored = await ReadStoredDocumentAsync(documentId, cancellationToken);
            if (stored is null)
            {
                return null;
            }

            if (!string.Equals(
                    Path.GetFileName(stored.StoredFileName),
                    stored.StoredFileName,
                    StringComparison.Ordinal))
            {
                throw new KnowledgeDocumentValidationException(
                    "Tên tệp lưu nội bộ không hợp lệ. Hãy tải lại tài liệu gốc.");
            }

            var storedPath = Path.Combine(FilesDirectory, stored.StoredFileName);
            if (!File.Exists(storedPath))
            {
                throw new KnowledgeDocumentValidationException(
                    "Không tìm thấy tệp gốc để lập chỉ mục lại. Hãy tải lại tài liệu.");
            }

            var previousStatus = stored.Status;
            await SetDocumentStatusAsync(documentId, "indexing", cancellationToken);

            ExtractedKnowledgeDocument extracted;
            IReadOnlyList<KnowledgeChunkDraft> chunks;
            try
            {
                await using var stream = new FileStream(
                    storedPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                extracted = await extractor.ExtractAsync(
                    stream,
                    stored.FileName,
                    ContentTypeFor(stored.FileType),
                    cancellationToken);
                chunks = _chunker.CreateChunks(extracted);
                if (chunks.Count == 0)
                {
                    throw new KnowledgeDocumentValidationException(
                        "Tài liệu không có nội dung đủ để lập chỉ mục.");
                }

                await ReplaceChunksAsync(documentId, extracted, chunks, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await TrySetDocumentStatusAsync(documentId, previousStatus, CancellationToken.None);
                throw;
            }
            catch
            {
                await TrySetDocumentStatusAsync(documentId, "error", CancellationToken.None);
                throw;
            }

            try
            {
                await embeddingIndex.IndexDocumentAsync(documentId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await TrySetDocumentStatusAsync(documentId, "index-error", CancellationToken.None);
                throw;
            }
            catch
            {
                await TrySetDocumentStatusAsync(documentId, "index-error", CancellationToken.None);
                throw;
            }

            logger.LogInformation(
                "Re-indexed knowledge document {DocumentId} into {ChunkCount} chunks.",
                documentId,
                chunks.Count);

            return await GetOneAsync(documentId, cancellationToken);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<BulkDeleteKnowledgeDocumentsResponse> DeleteManyAsync(
        IReadOnlyList<Guid>? documentIds,
        CancellationToken cancellationToken = default)
    {
        var ids = (documentIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            throw new KnowledgeDocumentValidationException(
                "Hãy chọn ít nhất một tài liệu để xóa.");
        }

        if (ids.Length > 100)
        {
            throw new KnowledgeDocumentValidationException(
                "Mỗi lần chỉ được xóa tối đa 100 tài liệu.");
        }

        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            var deleted = 0;
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await knowledgeStore.DeleteAsync(id, cancellationToken))
                {
                    deleted++;
                }
            }

            return new BulkDeleteKnowledgeDocumentsResponse(
                ids.Length,
                deleted,
                ids.Length - deleted);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<KnowledgeDocumentMetadataExport> ExportAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await GetAllAsync(cancellationToken);
        return new KnowledgeDocumentMetadataExport(
            1,
            DateTimeOffset.UtcNow,
            response.EmbeddingModel,
            response.Dimensions,
            response.Documents);
    }

    private async Task<KnowledgeDocumentManagementItem?> GetOneAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var embeddingStatus = await embeddingIndex.GetStatusAsync(cancellationToken);
        var documents = await ReadDocumentsAsync(
            embeddingStatus.EmbeddingModel,
            embeddingStatus.Dimensions,
            documentId,
            cancellationToken);
        return documents.FirstOrDefault();
    }

    private async Task<IReadOnlyList<KnowledgeDocumentManagementItem>> ReadDocumentsAsync(
        string embeddingModel,
        int dimensions,
        Guid? documentId,
        CancellationToken cancellationToken)
    {
        var documents = new List<KnowledgeDocumentManagementItem>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.Id, d.FileName, d.FileType, d.FileSize, d.CharacterCount,
                   d.Status, d.CreatedAt, d.PageCount, d.Sha256,
                   COUNT(DISTINCT c.Id) AS ChunkCount,
                   COUNT(DISTINCT CASE
                       WHEN e.ModelId = $modelId AND e.Dimensions = $dimensions
                       THEN e.ChunkId END) AS IndexedChunkCount
            FROM Documents d
            LEFT JOIN DocumentChunks c ON c.DocumentId = d.Id
            LEFT JOIN ChunkEmbeddings e ON e.ChunkId = c.Id
            WHERE ($documentId IS NULL OR d.Id = $documentId)
            GROUP BY d.Id, d.FileName, d.FileType, d.FileSize, d.CharacterCount,
                     d.Status, d.CreatedAt, d.PageCount, d.Sha256
            ORDER BY d.CreatedAt DESC;
            """;
        command.Parameters.AddWithValue("$modelId", embeddingModel);
        command.Parameters.AddWithValue("$dimensions", dimensions);
        command.Parameters.AddWithValue(
            "$documentId",
            documentId.HasValue ? documentId.Value.ToString("D") : DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var chunkCount = checked((int)Math.Min(reader.GetInt64(9), int.MaxValue));
            var indexedChunkCount = checked((int)Math.Min(reader.GetInt64(10), int.MaxValue));
            var status = reader.GetString(5);
            var indexStatus = status switch
            {
                "indexing" => "indexing",
                "error" => "error",
                "index-error" => "index-error",
                _ when chunkCount > 0 && indexedChunkCount >= chunkCount => "ready",
                _ => "needs-indexing"
            };

            documents.Add(new KnowledgeDocumentManagementItem(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetInt32(4),
                chunkCount,
                indexedChunkCount,
                Math.Max(0, chunkCount - indexedChunkCount),
                status,
                indexStatus,
                DateTimeOffset.Parse(
                    reader.GetString(6),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.GetString(8)));
        }

        return documents;
    }

    private async Task<StoredDocument?> ReadStoredDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT FileName, StoredFileName, FileType, Status
            FROM Documents
            WHERE Id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", documentId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredDocument(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3));
    }

    private async Task ReplaceChunksAsync(
        Guid documentId,
        ExtractedKnowledgeDocument extracted,
        IReadOnlyList<KnowledgeChunkDraft> chunks,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM DocumentChunks WHERE DocumentId = $documentId;";
            delete.Parameters.AddWithValue("$documentId", documentId.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO DocumentChunks (
                    DocumentId, ChunkIndex, Content, CharacterCount,
                    PageNumber, Heading, Section, TokenEstimate)
                VALUES (
                    $documentId, $chunkIndex, $content, $characterCount,
                    $pageNumber, $heading, $section, $tokenEstimate);
                """;
            var documentParameter = insert.Parameters.Add("$documentId", SqliteType.Text);
            var indexParameter = insert.Parameters.Add("$chunkIndex", SqliteType.Integer);
            var contentParameter = insert.Parameters.Add("$content", SqliteType.Text);
            var countParameter = insert.Parameters.Add("$characterCount", SqliteType.Integer);
            var pageParameter = insert.Parameters.Add("$pageNumber", SqliteType.Integer);
            var headingParameter = insert.Parameters.Add("$heading", SqliteType.Text);
            var sectionParameter = insert.Parameters.Add("$section", SqliteType.Text);
            var tokenParameter = insert.Parameters.Add("$tokenEstimate", SqliteType.Integer);

            for (var index = 0; index < chunks.Count; index++)
            {
                var chunk = chunks[index];
                documentParameter.Value = documentId.ToString("D");
                indexParameter.Value = index + 1;
                contentParameter.Value = chunk.Content;
                countParameter.Value = chunk.Content.Length;
                pageParameter.Value = chunk.PageNumber.HasValue ? chunk.PageNumber.Value : DBNull.Value;
                headingParameter.Value = chunk.Heading is null ? DBNull.Value : chunk.Heading;
                sectionParameter.Value = chunk.Section is null ? DBNull.Value : chunk.Section;
                tokenParameter.Value = chunk.TokenEstimate;
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Documents
                SET CharacterCount = $characterCount,
                    PageCount = $pageCount,
                    ExtractedText = $extractedText,
                    Status = 'ready'
                WHERE Id = $id;
                """;
            update.Parameters.AddWithValue("$characterCount", extracted.Text.Length);
            update.Parameters.AddWithValue(
                "$pageCount",
                extracted.PageCount.HasValue ? extracted.PageCount.Value : DBNull.Value);
            update.Parameters.AddWithValue("$extractedText", extracted.Text);
            update.Parameters.AddWithValue("$id", documentId.ToString("D"));
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    private async Task SetDocumentStatusAsync(
        Guid documentId,
        string status,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Documents SET Status = $status WHERE Id = $id;";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$id", documentId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task TrySetDocumentStatusAsync(
        Guid documentId,
        string status,
        CancellationToken cancellationToken)
    {
        try
        {
            await SetDocumentStatusAsync(documentId, status, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not update status {Status} for knowledge document {DocumentId}.",
                status,
                documentId);
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

    private static string NormalizeRenamedFileName(string? requestedFileName, string currentFileName)
    {
        var raw = requestedFileName?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            throw new KnowledgeDocumentValidationException("Tên tài liệu không được để trống.");
        }

        if (raw.Length > 180)
        {
            throw new KnowledgeDocumentValidationException(
                "Tên tài liệu không được dài hơn 180 ký tự.");
        }

        if (!string.Equals(Path.GetFileName(raw), raw, StringComparison.Ordinal)
            || raw.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new KnowledgeDocumentValidationException(
                "Tên tài liệu chứa ký tự hoặc đường dẫn không hợp lệ.");
        }

        var currentExtension = Path.GetExtension(currentFileName);
        var requestedExtension = Path.GetExtension(raw);
        if (requestedExtension.Length == 0)
        {
            raw += currentExtension;
        }
        else if (!requestedExtension.Equals(currentExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new KnowledgeDocumentValidationException(
                $"Không thể đổi định dạng tài liệu khi đổi tên. Hãy giữ phần mở rộng {currentExtension}.");
        }

        if (string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(raw)))
        {
            throw new KnowledgeDocumentValidationException("Tên tài liệu không hợp lệ.");
        }

        return raw;
    }

    private static string ContentTypeFor(string fileType) => fileType.ToUpperInvariant() switch
    {
        "PDF" => "application/pdf",
        "DOCX" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "MD" => "text/markdown",
        _ => "text/plain"
    };

    private sealed record StoredDocument(
        string FileName,
        string StoredFileName,
        string FileType,
        string Status);
}
