using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IKnowledgeDocumentStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeDocumentResponse>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<KnowledgeDocumentResponse> AddAsync(
        IFormFile file,
        CancellationToken cancellationToken = default);

    Task<KnowledgeSearchResponse> SearchAsync(
        string query,
        int limit = 5,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}

public sealed class KnowledgeDocumentValidationException(string message) : Exception(message);

public sealed class DuplicateKnowledgeDocumentException(string message) : Exception(message);
