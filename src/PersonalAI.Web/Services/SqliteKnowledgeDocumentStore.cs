using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class SqliteKnowledgeDocumentStore(
    ILogger<SqliteKnowledgeDocumentStore> logger,
    KnowledgeDocumentExtractor extractor) : IKnowledgeDocumentStore
{
    public const long MaximumFileSize = 10 * 1024 * 1024;

    private const int TargetChunkSize = 1_200;
    private const int MaximumChunkSize = 1_500;
    private const int MinimumChunkSize = 600;
    private const int ChunkOverlap = 180;
    private const int MaximumSearchLength = 200;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".pdf", ".docx" };

    private static readonly HashSet<string> SearchStopWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "bạn", "biết", "các", "có", "của", "cho", "đó", "được", "gì",
            "giúp", "hãy", "không", "là", "mình", "một", "này", "những",
            "nói", "tôi", "trả", "trong", "và", "về"
        };

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly string _knowledgeDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalAI",
        "Knowledge");
    private bool _initialized;

    private string DatabasePath => Path.Combine(_knowledgeDirectory, "personal-ai.db");
    private string FilesDirectory => Path.Combine(_knowledgeDirectory, "files");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(FilesDirectory);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS Documents (
                        Id TEXT PRIMARY KEY,
                        FileName TEXT NOT NULL,
                        StoredFileName TEXT NOT NULL,
                        FileType TEXT NOT NULL,
                        FileSize INTEGER NOT NULL,
                        Sha256 TEXT NOT NULL UNIQUE,
                        CharacterCount INTEGER NOT NULL,
                        PageCount INTEGER NULL,
                        ExtractedText TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL
                    );

                    CREATE INDEX IF NOT EXISTS IX_Documents_CreatedAt
                    ON Documents (CreatedAt DESC);

                    CREATE TABLE IF NOT EXISTS DocumentChunks (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        DocumentId TEXT NOT NULL,
                        ChunkIndex INTEGER NOT NULL,
                        Content TEXT NOT NULL,
                        CharacterCount INTEGER NOT NULL,
                        FOREIGN KEY (DocumentId) REFERENCES Documents(Id) ON DELETE CASCADE,
                        UNIQUE (DocumentId, ChunkIndex)
                    );

                    CREATE INDEX IF NOT EXISTS IX_DocumentChunks_DocumentId
                    ON DocumentChunks (DocumentId, ChunkIndex);

                    CREATE VIRTUAL TABLE IF NOT EXISTS DocumentChunksFts USING fts5(
                        Content,
                        content='DocumentChunks',
                        content_rowid='Id',
                        tokenize='unicode61 remove_diacritics 2'
                    );

                    CREATE TRIGGER IF NOT EXISTS DocumentChunks_AfterInsert
                    AFTER INSERT ON DocumentChunks BEGIN
                        INSERT INTO DocumentChunksFts(rowid, Content)
                        VALUES (new.Id, new.Content);
                    END;

                    CREATE TRIGGER IF NOT EXISTS DocumentChunks_AfterDelete
                    AFTER DELETE ON DocumentChunks BEGIN
                        INSERT INTO DocumentChunksFts(DocumentChunksFts, rowid, Content)
                        VALUES ('delete', old.Id, old.Content);
                    END;

                    CREATE TRIGGER IF NOT EXISTS DocumentChunks_AfterUpdate
                    AFTER UPDATE ON DocumentChunks BEGIN
                        INSERT INTO DocumentChunksFts(DocumentChunksFts, rowid, Content)
                        VALUES ('delete', old.Id, old.Content);
                        INSERT INTO DocumentChunksFts(rowid, Content)
                        VALUES (new.Id, new.Content);
                    END;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await EnsurePageCountColumnAsync(connection, cancellationToken);
            await BackfillMissingChunksAsync(connection, cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<IReadOnlyList<KnowledgeDocumentResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        var documents = new List<KnowledgeDocumentResponse>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.Id, d.FileName, d.FileType, d.FileSize, d.CharacterCount,
                   d.PageCount, COUNT(c.Id), d.Status, d.CreatedAt
            FROM Documents d
            LEFT JOIN DocumentChunks c ON c.DocumentId = d.Id
            GROUP BY d.Id, d.FileName, d.FileType, d.FileSize,
                     d.CharacterCount, d.PageCount, d.Status, d.CreatedAt
            ORDER BY d.CreatedAt DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(ReadPublicDocument(reader));
        }

        return documents;
    }

    public async Task<KnowledgeDocumentResponse> AddAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        ValidateFile(file);
        await InitializeAsync(cancellationToken);

        var safeFileName = Path.GetFileName(file.FileName).Trim();
        var extension = Path.GetExtension(safeFileName).ToLowerInvariant();
        var fileType = extension.TrimStart('.').ToUpperInvariant();
        var documentId = Guid.NewGuid();
        var storedFileName = $"{documentId:N}{extension}";
        var storedPath = Path.Combine(FilesDirectory, storedFileName);
        var createdAt = DateTimeOffset.UtcNow;
        var hash = await CalculateHashAsync(file, cancellationToken);

        if (await HashExistsAsync(hash, cancellationToken))
        {
            throw new DuplicateKnowledgeDocumentException("Tài liệu này đã có trong kho dữ liệu.");
        }

        var extracted = await extractor.ExtractAsync(file, cancellationToken);
        var text = extracted.Text;
        var chunks = CreateChunks(text);
        if (chunks.Count == 0)
        {
            throw new KnowledgeDocumentValidationException("Tài liệu không có nội dung đủ để lập chỉ mục.");
        }

        await SaveOriginalFileAsync(file, storedPath, cancellationToken);

        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO Documents (
                        Id, FileName, StoredFileName, FileType, FileSize, Sha256,
                        CharacterCount, PageCount, ExtractedText, Status, CreatedAt)
                    VALUES (
                        $id, $fileName, $storedFileName, $fileType, $fileSize, $sha256,
                        $characterCount, $pageCount, $extractedText, $status, $createdAt);
                    """;
                command.Parameters.AddWithValue("$id", documentId.ToString("D"));
                command.Parameters.AddWithValue("$fileName", safeFileName);
                command.Parameters.AddWithValue("$storedFileName", storedFileName);
                command.Parameters.AddWithValue("$fileType", fileType);
                command.Parameters.AddWithValue("$fileSize", file.Length);
                command.Parameters.AddWithValue("$sha256", hash);
                command.Parameters.AddWithValue("$characterCount", text.Length);
                command.Parameters.AddWithValue(
                    "$pageCount",
                    extracted.PageCount.HasValue ? extracted.PageCount.Value : DBNull.Value);
                command.Parameters.AddWithValue("$extractedText", text);
                command.Parameters.AddWithValue("$status", "ready");
                command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O", CultureInfo.InvariantCulture));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await InsertChunksAsync(
                connection,
                transaction,
                documentId.ToString("D"),
                chunks,
                cancellationToken);
            transaction.Commit();
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            TryDeleteFile(storedPath);
            throw new DuplicateKnowledgeDocumentException("Tài liệu này đã có trong kho dữ liệu.");
        }
        catch
        {
            TryDeleteFile(storedPath);
            throw;
        }

        return new KnowledgeDocumentResponse(
            documentId,
            safeFileName,
            fileType,
            file.Length,
            text.Length,
            chunks.Count,
            "ready",
            createdAt,
            extracted.PageCount);
    }

    public async Task<KnowledgeSearchResponse> SearchAsync(
        string query,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = ValidateAndNormalizeSearchQuery(query);
        var ftsQuery = BuildFtsQuery(normalizedQuery);
        var safeLimit = Math.Clamp(limit, 1, 10);
        await InitializeAsync(cancellationToken);

        var results = new List<KnowledgeSearchResult>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.DocumentId, d.FileName, c.ChunkIndex, c.Content
            FROM DocumentChunksFts
            JOIN DocumentChunks c ON c.Id = DocumentChunksFts.rowid
            JOIN Documents d ON d.Id = c.DocumentId
            WHERE DocumentChunksFts MATCH $query
            ORDER BY bm25(DocumentChunksFts), d.CreatedAt DESC, c.ChunkIndex
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$query", ftsQuery);
        command.Parameters.AddWithValue("$limit", safeLimit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new KnowledgeSearchResult(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3)));
        }

        return new KnowledgeSearchResponse(normalizedQuery, results.Count, results);
    }

    public async Task<bool> DeleteAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        string? storedFileName;
        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = "SELECT StoredFileName FROM Documents WHERE Id = $id;";
            selectCommand.Parameters.AddWithValue("$id", documentId.ToString("D"));
            storedFileName = (string?)await selectCommand.ExecuteScalarAsync(cancellationToken);
        }

        if (storedFileName is null)
        {
            return false;
        }

        using (var transaction = connection.BeginTransaction())
        {
            await using var deleteChunksCommand = connection.CreateCommand();
            deleteChunksCommand.Transaction = transaction;
            deleteChunksCommand.CommandText = "DELETE FROM DocumentChunks WHERE DocumentId = $id;";
            deleteChunksCommand.Parameters.AddWithValue("$id", documentId.ToString("D"));
            await deleteChunksCommand.ExecuteNonQueryAsync(cancellationToken);

            await using var deleteDocumentCommand = connection.CreateCommand();
            deleteDocumentCommand.Transaction = transaction;
            deleteDocumentCommand.CommandText = "DELETE FROM Documents WHERE Id = $id;";
            deleteDocumentCommand.Parameters.AddWithValue("$id", documentId.ToString("D"));
            await deleteDocumentCommand.ExecuteNonQueryAsync(cancellationToken);
            transaction.Commit();
        }

        if (string.Equals(Path.GetFileName(storedFileName), storedFileName, StringComparison.Ordinal))
        {
            TryDeleteFile(Path.Combine(FilesDirectory, storedFileName));
        }

        return true;
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

    private static async Task EnsurePageCountColumnAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var hasPageCount = false;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(Documents);";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "PageCount", StringComparison.OrdinalIgnoreCase))
                {
                    hasPageCount = true;
                    break;
                }
            }
        }

        if (hasPageCount)
        {
            return;
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = "ALTER TABLE Documents ADD COLUMN PageCount INTEGER NULL;";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task BackfillMissingChunksAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var documents = new List<(string Id, string Text)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT d.Id, d.ExtractedText
                FROM Documents d
                WHERE NOT EXISTS (
                    SELECT 1 FROM DocumentChunks c WHERE c.DocumentId = d.Id
                );
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                documents.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        if (documents.Count == 0)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        foreach (var document in documents)
        {
            await InsertChunksAsync(
                connection,
                transaction,
                document.Id,
                CreateChunks(document.Text),
                cancellationToken);
        }
        transaction.Commit();
    }

    private static async Task InsertChunksAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string documentId,
        IReadOnlyList<string> chunks,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO DocumentChunks (DocumentId, ChunkIndex, Content, CharacterCount)
            VALUES ($documentId, $chunkIndex, $content, $characterCount);
            """;
        var documentParameter = command.Parameters.Add("$documentId", SqliteType.Text);
        var indexParameter = command.Parameters.Add("$chunkIndex", SqliteType.Integer);
        var contentParameter = command.Parameters.Add("$content", SqliteType.Text);
        var countParameter = command.Parameters.Add("$characterCount", SqliteType.Integer);

        for (var index = 0; index < chunks.Count; index++)
        {
            documentParameter.Value = documentId;
            indexParameter.Value = index + 1;
            contentParameter.Value = chunks[index];
            countParameter.Value = chunks[index].Length;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<bool> HashExistsAsync(string hash, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM Documents WHERE Sha256 = $sha256 LIMIT 1;";
        command.Parameters.AddWithValue("$sha256", hash);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static IReadOnlyList<string> CreateChunks(string source)
    {
        var text = source
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        var chunks = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            var end = Math.Min(start + TargetChunkSize, text.Length);
            if (end < text.Length)
            {
                end = FindChunkBoundary(text, start, end);
            }

            var chunk = text[start..end].Trim();
            if (chunk.Length > 0)
            {
                chunks.Add(chunk);
            }

            if (end >= text.Length)
            {
                break;
            }

            start = Math.Max(start + 1, end - ChunkOverlap);
        }

        return chunks;
    }

    private static int FindChunkBoundary(string text, int start, int desiredEnd)
    {
        var maximumEnd = Math.Min(start + MaximumChunkSize, text.Length);
        for (var index = desiredEnd; index < maximumEnd; index++)
        {
            if (IsStrongBoundary(text, index))
            {
                return index + 1;
            }
        }

        var minimumEnd = Math.Min(start + MinimumChunkSize, desiredEnd);
        for (var index = desiredEnd; index > minimumEnd; index--)
        {
            if (IsStrongBoundary(text, index - 1))
            {
                return index;
            }
        }

        for (var index = desiredEnd; index > minimumEnd; index--)
        {
            if (char.IsWhiteSpace(text[index - 1]))
            {
                return index;
            }
        }

        return desiredEnd;
    }

    private static bool IsStrongBoundary(string text, int index)
    {
        var character = text[index];
        return character == '\n'
            || ((character is '.' or '?' or '!')
                && (index + 1 >= text.Length || char.IsWhiteSpace(text[index + 1])));
    }

    private static string ValidateAndNormalizeSearchQuery(string query)
    {
        var normalized = (query ?? string.Empty).Trim();
        if (normalized.Length < 2)
        {
            throw new KnowledgeDocumentValidationException(
                "Hãy nhập ít nhất 2 ký tự để tìm trong dữ liệu.");
        }

        if (normalized.Length > MaximumSearchLength)
        {
            throw new KnowledgeDocumentValidationException(
                "Nội dung tìm kiếm không được dài hơn 200 ký tự.");
        }

        return normalized;
    }

    private static string BuildFtsQuery(string query)
    {
        var allTokens = Regex.Matches(
                query.Normalize(NormalizationForm.FormKC),
                @"[\p{L}\p{N}]+")
            .Cast<Match>()
            .Select(match => match.Value)
            .Where(token => token.Length > 0)
            .Take(10)
            .ToArray();

        if (allTokens.Length == 0)
        {
            throw new KnowledgeDocumentValidationException(
                "Nội dung tìm kiếm phải có chữ hoặc số.");
        }

        var meaningfulTokens = allTokens
            .Where(token => !SearchStopWords.Contains(token))
            .ToArray();
        var tokens = meaningfulTokens.Length > 0 ? meaningfulTokens : allTokens;

        return string.Join(" AND ", tokens.Select(token => $"\"{token}\"*"));
    }

    private static void ValidateFile(IFormFile file)
    {
        if (file.Length <= 0)
        {
            throw new KnowledgeDocumentValidationException("Hãy chọn một tệp có nội dung.");
        }

        if (file.Length > MaximumFileSize)
        {
            throw new KnowledgeDocumentValidationException("Tệp không được lớn hơn 10 MB.");
        }

        var safeFileName = Path.GetFileName(file.FileName).Trim();
        if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName.Length > 180)
        {
            throw new KnowledgeDocumentValidationException("Tên tệp không hợp lệ hoặc quá dài.");
        }

        if (!AllowedExtensions.Contains(Path.GetExtension(safeFileName)))
        {
            throw new KnowledgeDocumentValidationException(
                "Phiên bản 0.7.0 chỉ nhận PDF, DOCX, TXT và Markdown (.md).");
        }
    }

    private static async Task<string> CalculateHashAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static async Task SaveOriginalFileAsync(
        IFormFile file,
        string path,
        CancellationToken cancellationToken)
    {
        await using var source = file.OpenReadStream();
        await using var destination = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            useAsync: true);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static KnowledgeDocumentResponse ReadPublicDocument(SqliteDataReader reader)
    {
        return new KnowledgeDocumentResponse(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetInt32(4),
            checked((int)reader.GetInt64(6)),
            reader.GetString(7),
            DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture),
            reader.IsDBNull(5) ? null : reader.GetInt32(5));
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Không thể xóa tệp dữ liệu cục bộ {FileName}.",
                Path.GetFileName(path));
        }
    }
}
