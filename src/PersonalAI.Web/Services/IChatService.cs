using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IChatService
{
    bool IsConfigured { get; }
    string Model { get; }
    Task<string> ReplyAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);
}
