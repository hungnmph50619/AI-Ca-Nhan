namespace PersonalAI.Web.Models;

public static class PersonalWorkspaceIds
{
    public const string Personal = "personal";
    public const string Work = "work";
    public const string Study = "study";
    public const string PersonalAi = "ai-ca-nhan";
    public const string Travel = "travel";
}

public sealed record PersonalWorkspace(
    string Id,
    string Name,
    string Description,
    bool IsBuiltIn,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> ActiveModules,
    IReadOnlyList<string> ReservedModules);

public sealed record WorkspaceListResponse(
    string CurrentWorkspaceId,
    int MaximumWorkspaces,
    IReadOnlyList<PersonalWorkspace> Workspaces);

public sealed record CreatePersonalWorkspaceRequest(
    string? Name,
    string? Description = null);

public sealed record UpdatePersonalWorkspaceRequest(
    string? Name,
    string? Description = null);
