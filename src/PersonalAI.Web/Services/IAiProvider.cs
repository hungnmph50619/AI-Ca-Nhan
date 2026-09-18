using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IAiProvider
{
    string Name { get; }
    string Model { get; }
    bool IsConfigured { get; }

    Task<string> ReplyAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken);

    Task<ProviderFunctionCallDecision?> ProposeFunctionCallAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ProviderFunctionDefinition> functions,
        CancellationToken cancellationToken);
}
