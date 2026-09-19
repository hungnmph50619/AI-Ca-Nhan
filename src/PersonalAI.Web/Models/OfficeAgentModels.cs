namespace PersonalAI.Web.Models;

public static class OfficeDraftKinds
{
    public const string Email = "email";
    public const string Memo = "memo";
    public const string Agenda = "agenda";
    public const string Minutes = "minutes";
    public const string Summary = "summary";
    public static readonly IReadOnlyList<string> All =
        [Email, Memo, Agenda, Minutes, Summary];
}

public sealed record OfficeDraft(
    string Kind,
    string Title,
    string Body,
    IReadOnlyList<string> ActionItems,
    IReadOnlyList<string> MissingInformation,
    string Status,
    bool Sent,
    bool Persisted,
    bool CreatedCalendarEvent,
    bool CreatedTasks,
    bool ModifiedDocuments,
    DateTimeOffset CreatedAt);

public sealed record OfficeAgentStatusResponse(
    string Version,
    string AgentId,
    IReadOnlyList<string> SupportedKinds,
    int MaximumBodyCharacters,
    int MaximumActionItems,
    bool StructuredOutput,
    bool ParserSelfTestPassed,
    bool SendsEmail,
    bool WritesDocuments,
    bool CreatesCalendarEvents,
    bool CreatesTasks,
    bool PersistsDrafts,
    bool ToolExecutionEnabled,
    bool RequiresExplicitInvocation,
    string NextStage);
