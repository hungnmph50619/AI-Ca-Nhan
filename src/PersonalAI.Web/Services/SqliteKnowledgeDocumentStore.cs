using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class SqliteKnowledgeDocumentStore(
    ILogger<SqliteKnowledgeDocumentStore> logger) : IKnowledgeDocumentStore
{
    public const long MaximumFileSize = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md" };

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

            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Documents (
                    Id TEXT PRIMARY KEY,
                    FileName TEXT NOT NULL,
                    StoredFileName TEXT NOT NULL,
                    FileType TEXT NOT NULL,
                    FileSize INTEGER NOT NULL,
                    Sha256 TEXT NOT NULL UNIQUE,
                    CharacterCount INTEGER NOT NULL,
                    ExtractedText TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_Documents_CreatedAt
                ON Documents (CreatedAt DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

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
            SELECT Id, FileName, FileType, FileSize, CharacterCount, Status, CreatedAt
            FROM Documents
            ORDER BY CreatedAt DESC;
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
        var documentId = Guid.NewGuid();
        var storedFileName = $"{documentId:N}{extension}";
        var storedPath = Path.Combine(FilesDirectory, storedFileName);
        var createdAt = DateTimeOffset.UtcNow;
        var hash = await CalculateHashAsync(file, cancellationToken);
        var text = await ReadTextAsync(file, cancellationToken);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new KnowledgeDocumentValidationException("Tài liệu không có nội dung văn bản.");
        }

        if (text.IndexOf('\0') >= 0)
        {
            throw new KnowledgeDocumentValidationException(
                "Tệp có dữ liệu nhị phân và không phải TXT hoặc Markdown hợp lệ.");
        }

        if (await HashExistsAsync(hash, cancellationToken))
        {
            throw new DuplicateKnowledgeDocumentException("Tài liệu này đã có trong kho dữ liệu.");
        }

        await File.WriteAllTextAsync(
            storedPath,
            text,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Documents (
                    Id, FileName, StoredFileName, FileType, FileSize, Sha256,
                    CharacterCount, ExtractedText, Status, CreatedAt)
                VALUES (
                    $id, $fileName, $storedFileName, $fileType, $fileSize, $sha256,
                    $characterCount, $extractedText, $status, $createdAt);
                """;
            command.Parameters.AddWithValue("$id", documentId.ToString("D"));
            command.Parameters.AddWithValue("$fileName", safeFileName);
            command.Parameters.AddWithValue("$storedFileName", storedFileName);
            command.Parameters.AddWithValue("$fileType", extension.TrimStart('.').ToUpperInvariant());
            command.Parameters.AddWithValue("$fileSize", file.Length);
            command.Parameters.AddWithValue("$sha256", hash);
            command.Parameters.AddWithValue("$characterCount", text.Length);
            command.Parameters.AddWithValue("$extractedText", text);
            command.Parameters.AddWithValue("$status", "ready");
            command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
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
            extension.TrimStart('.').ToUpperInvariant(),
            file.Length,
            text.Length,
            "ready",
            createdAt);
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

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.CommandText = "DELETE FROM Documents WHERE Id = $id;";
            deleteCommand.Parameters.AddWithValue("$id", documentId.ToString("D"));
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (Path.GetFileName(storedFileName).Equals(storedFileName, StringComparison.Ordinal))
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
            Cache = SqliteCacheMode.Shared
        }.ToString();

        return new SqliteConnection(connectionString);
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
                "Phiên bản 0.5.1 chỉ nhận tệp TXT và Markdown (.md).");
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

    private static async Task<string> ReadTextAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = file.OpenReadStream();
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            throw new KnowledgeDocumentValidationException(
                "Không đọc được bảng mã của tệp. Hãy lưu tệp dưới dạng UTF-8 rồi thử lại.");
        }
    }

    private static KnowledgeDocumentResponse ReadPublicDocument(SqliteDataReader reader)
    {
        return new KnowledgeDocumentResponse(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetInt32(4),
            reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture));
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
            logger.LogWarning(exception, "Không thể xóa tệp dữ liệu cục bộ {FileName}.", Path.GetFileName(path));
        }
    }
}
