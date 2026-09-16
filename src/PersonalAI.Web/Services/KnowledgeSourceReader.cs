using Microsoft.Data.Sqlite;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class KnowledgeSourceReader
{
    private readonly string _databasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalAI",
        "Knowledge",
        "personal-ai.db");

    public async Task<KnowledgeSearchResult?> GetChunkAsync(
        Guid documentId,
        int chunkIndex,
        CancellationToken cancellationToken = default)
    {
        if (chunkIndex <= 0 || !File.Exists(_databasePath))
        {
            return null;
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.DocumentId, d.FileName, c.ChunkIndex, c.Content
            FROM DocumentChunks c
            JOIN Documents d ON d.Id = c.DocumentId
            WHERE c.DocumentId = $documentId
              AND c.ChunkIndex = $chunkIndex
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$documentId", documentId.ToString("D"));
        command.Parameters.AddWithValue("$chunkIndex", chunkIndex);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new KnowledgeSearchResult(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetString(3));
    }
}
