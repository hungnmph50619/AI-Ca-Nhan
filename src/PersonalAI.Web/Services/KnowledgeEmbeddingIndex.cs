using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IKnowledgeEmbeddingIndex
{
    Task IndexDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<KnowledgeSemanticSearchResponse> SearchAsync(
        string query,
        int limit = 5,
        CancellationToken cancellationToken = default);

    Task<KnowledgeEmbeddingStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);
}

public sealed class KnowledgeEmbeddingIndex(
    IKnowledgeDocumentStore knowledgeStore,
    IKnowledgeEmbeddingService embeddingService,
    ILogger<KnowledgeEmbeddingIndex> logger) : IKnowledgeEmbeddingIndex
{
    private const int MaximumSearchLength = 500;
    private const double MinimumSimilarity = 0.05;

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly SemaphoreSlim _backfillLock = new(1, 1);
    private readonly string _databasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalAI",
        "Knowledge",
        "personal-ai.db");
    private bool _initialized;
    private bool _backfillCompleted;

    public async Task IndexDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var chunks = new List<(long Id, string Content)>();

        await using (var connection = CreateConnection())
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, Content
                FROM DocumentChunks
                WHERE DocumentId = $documentId
                ORDER BY ChunkIndex;
                """;
            command.Parameters.AddWithValue("$documentId", documentId.ToString("D"));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                chunks.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        if (chunks.Count == 0)
        {
            return;
        }

        await UpsertEmbeddingsAsync(chunks, cancellationToken);
    }

    public async Task<KnowledgeSemanticSearchResponse> SearchAsync(
        string query,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = (query ?? string.Empty).Trim();
        if (normalizedQuery.Length < 2)
        {
            throw new KnowledgeDocumentValidationException(
                "Hãy nhập ít nhất 2 ký tự để tìm ngữ nghĩa trong dữ liệu.");
        }

        if (normalizedQuery.Length > MaximumSearchLength)
        {
            throw new KnowledgeDocumentValidationException(
                $"Nội dung tìm kiếm ngữ nghĩa không được dài hơn {MaximumSearchLength} ký tự.");
        }

        await EnsureBackfillAsync(cancellationToken);
        var queryVector = embeddingService.Embed(normalizedQuery);
        if (!queryVector.Any(value => Math.Abs(value) > float.Epsilon))
        {
            return new KnowledgeSemanticSearchResponse(
                normalizedQuery,
                0,
                embeddingService.ModelId,
                embeddingService.Dimensions,
                []);
        }

        var safeLimit = Math.Clamp(limit, 1, 10);
        var topResults = new PriorityQueue<KnowledgeSearchResult, double>();

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.DocumentId, d.FileName, c.ChunkIndex, c.Content,
                   c.PageNumber, c.Heading, c.Section, c.TokenEstimate, e.Vector
            FROM ChunkEmbeddings e
            JOIN DocumentChunks c ON c.Id = e.ChunkId
            JOIN Documents d ON d.Id = c.DocumentId
            WHERE e.ModelId = $modelId
              AND e.Dimensions = $dimensions;
            """;
        command.Parameters.AddWithValue("$modelId", embeddingService.ModelId);
        command.Parameters.AddWithValue("$dimensions", embeddingService.Dimensions);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = (byte[])reader[8];
            var vector = DeserializeVector(bytes, embeddingService.Dimensions);
            if (vector is null)
            {
                continue;
            }

            var similarity = embeddingService.CosineSimilarity(queryVector, vector);
            if (similarity < MinimumSimilarity)
            {
                continue;
            }

            var result = new KnowledgeSearchResult(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                Math.Round(similarity, 6));

            if (topResults.Count < safeLimit)
            {
                topResults.Enqueue(result, similarity);
                continue;
            }

            if (topResults.TryPeek(out _, out var lowestSimilarity)
                && similarity > lowestSimilarity)
            {
                topResults.Dequeue();
                topResults.Enqueue(result, similarity);
            }
        }

        var results = new List<KnowledgeSearchResult>(topResults.Count);
        while (topResults.TryDequeue(out var result, out _))
        {
            results.Add(result);
        }
        results.Reverse();

        return new KnowledgeSemanticSearchResponse(
            normalizedQuery,
            results.Count,
            embeddingService.ModelId,
            embeddingService.Dimensions,
            results);
    }

    public async Task<KnowledgeEmbeddingStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureBackfillAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        long totalChunks;
        await using (var totalCommand = connection.CreateCommand())
        {
            totalCommand.CommandText = "SELECT COUNT(*) FROM DocumentChunks;";
            totalChunks = (long)(await totalCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        long indexedChunks;
        await using (var indexedCommand = connection.CreateCommand())
        {
            indexedCommand.CommandText = """
                SELECT COUNT(*)
                FROM ChunkEmbeddings
                WHERE ModelId = $modelId
                  AND Dimensions = $dimensions;
                """;
            indexedCommand.Parameters.AddWithValue("$modelId", embeddingService.ModelId);
            indexedCommand.Parameters.AddWithValue("$dimensions", embeddingService.Dimensions);
            indexedChunks = (long)(await indexedCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        return new KnowledgeEmbeddingStatus(
            embeddingService.ModelId,
            embeddingService.Dimensions,
            checked((int)Math.Min(totalChunks, int.MaxValue)),
            checked((int)Math.Min(indexedChunks, int.MaxValue)),
            checked((int)Math.Min(Math.Max(0, totalChunks - indexedChunks), int.MaxValue)),
            true);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
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

            await knowledgeStore.InitializeAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS ChunkEmbeddings (
                    ChunkId INTEGER PRIMARY KEY,
                    ModelId TEXT NOT NULL,
                    Dimensions INTEGER NOT NULL,
                    ContentSha256 TEXT NOT NULL,
                    Vector BLOB NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (ChunkId) REFERENCES DocumentChunks(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_ChunkEmbeddings_Model
                ON ChunkEmbeddings (ModelId, Dimensions);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task EnsureBackfillAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (_backfillCompleted)
        {
            return;
        }

        await _backfillLock.WaitAsync(cancellationToken);
        try
        {
            if (_backfillCompleted)
            {
                return;
            }

            var staleChunks = new List<(long Id, string Content)>();
            await using (var connection = CreateConnection())
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT c.Id, c.Content, e.ModelId, e.Dimensions, e.ContentSha256
                    FROM DocumentChunks c
                    LEFT JOIN ChunkEmbeddings e ON e.ChunkId = c.Id;
                    """;

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var chunkId = reader.GetInt64(0);
                    var content = reader.GetString(1);
                    var contentHash = CalculateContentHash(content);
                    var isCurrent = !reader.IsDBNull(2)
                        && string.Equals(reader.GetString(2), embeddingService.ModelId, StringComparison.Ordinal)
                        && !reader.IsDBNull(3)
                        && reader.GetInt32(3) == embeddingService.Dimensions
                        && !reader.IsDBNull(4)
                        && string.Equals(reader.GetString(4), contentHash, StringComparison.Ordinal);
                    if (!isCurrent)
                    {
                        staleChunks.Add((chunkId, content));
                    }
                }
            }

            if (staleChunks.Count > 0)
            {
                logger.LogInformation(
                    "Indexing {ChunkCount} knowledge chunks with embedding model {ModelId}.",
                    staleChunks.Count,
                    embeddingService.ModelId);
                await UpsertEmbeddingsAsync(staleChunks, cancellationToken);
            }

            _backfillCompleted = true;
        }
        finally
        {
            _backfillLock.Release();
        }
    }

    private async Task UpsertEmbeddingsAsync(
        IReadOnlyList<(long Id, string Content)> chunks,
        CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ChunkEmbeddings (
                ChunkId, ModelId, Dimensions, ContentSha256, Vector, UpdatedAt)
            VALUES (
                $chunkId, $modelId, $dimensions, $contentSha256, $vector, $updatedAt)
            ON CONFLICT(ChunkId) DO UPDATE SET
                ModelId = excluded.ModelId,
                Dimensions = excluded.Dimensions,
                ContentSha256 = excluded.ContentSha256,
                Vector = excluded.Vector,
                UpdatedAt = excluded.UpdatedAt;
            """;
        var chunkIdParameter = command.Parameters.Add("$chunkId", SqliteType.Integer);
        var modelParameter = command.Parameters.Add("$modelId", SqliteType.Text);
        var dimensionsParameter = command.Parameters.Add("$dimensions", SqliteType.Integer);
        var hashParameter = command.Parameters.Add("$contentSha256", SqliteType.Text);
        var vectorParameter = command.Parameters.Add("$vector", SqliteType.Blob);
        var updatedAtParameter = command.Parameters.Add("$updatedAt", SqliteType.Text);

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = embeddingService.Embed(chunk.Content);
            chunkIdParameter.Value = chunk.Id;
            modelParameter.Value = embeddingService.ModelId;
            dimensionsParameter.Value = embeddingService.Dimensions;
            hashParameter.Value = CalculateContentHash(chunk.Content);
            vectorParameter.Value = SerializeVector(vector);
            updatedAtParameter.Value = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    private SqliteConnection CreateConnection()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true
        }.ToString();
        return new SqliteConnection(connectionString);
    }

    private static string CalculateContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private static byte[] SerializeVector(ReadOnlySpan<float> vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        for (var index = 0; index < vector.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(index * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(vector[index]));
        }
        return bytes;
    }

    private static float[]? DeserializeVector(byte[] bytes, int dimensions)
    {
        if (bytes.Length != dimensions * sizeof(float))
        {
            return null;
        }

        var vector = new float[dimensions];
        for (var index = 0; index < dimensions; index++)
        {
            var bits = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(index * sizeof(float), sizeof(float)));
            vector[index] = BitConverter.Int32BitsToSingle(bits);
        }
        return vector;
    }
}
