namespace PersonalAI.Web.Models;

public static class ContextSelectionKinds
{
    public const string Memory = "memory";
    public const string Document = "document";
    public const string Task = "task";
}

public sealed record ContextSelectionItem(
    string Kind,
    string Id,
    string Label,
    string Reason,
    double? Score,
    int CharacterCount);

public sealed record ContextBudgetReport(
    int MaximumCharacters,
    int UsedCharacters,
    int MemoryCharacters,
    int DocumentCharacters,
    int TaskCharacters);

public sealed record ContextSelectionReport(
    string WorkspaceId,
    string Strategy,
    int SelectedMemories,
    int SelectedDocuments,
    int SelectedTasks,
    ContextBudgetReport Budget,
    IReadOnlyList<ContextSelectionItem> Items);

public sealed record ContextPreviewRequest(
    IReadOnlyList<ChatMessage> Messages,
    bool UseKnowledge = true,
    string KnowledgeMode = "normal",
    bool UseMemory = true,
    bool UseTaskContext = true);

public sealed record ContextPreviewResponse(
    ContextSelectionReport Context,
    IReadOnlyList<ChatSource> Sources);
