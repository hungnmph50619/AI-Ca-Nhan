namespace PersonalAI.Web.Teams;

public sealed record AgentRole(string Id, string Name, string Purpose);

public sealed record TeamProfile(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<AgentRole> Agents);
