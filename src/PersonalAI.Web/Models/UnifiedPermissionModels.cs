namespace PersonalAI.Web.Models;

public static class UnifiedPermissionEffects
{
    public const string Allow = "allow";
    public const string Deny = "deny";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>([Allow, Deny], StringComparer.Ordinal);
}

public static class UnifiedPermissionSubjectTypes
{
    public const string User = "user";
    public const string Device = "device";
    public const string Companion = "companion";
    public const string Agent = "agent";
    public const string System = "system";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [User, Device, Companion, Agent, System],
            StringComparer.Ordinal);
}

public sealed record SetUnifiedPermissionRequest(
    string SubjectType,
    string SubjectId,
    string Resource,
    string Action,
    string Effect,
    DateTimeOffset? ExpiresAt = null,
    bool ConfirmChange = false);

public sealed record EvaluateUnifiedPermissionRequest(
    string SubjectType,
    string SubjectId,
    string Resource,
    string Action);

public sealed record RevokeUnifiedPermissionRequest(
    bool ConfirmRevoke = false);

public sealed record UnifiedPermissionRule(
    Guid Id,
    string WorkspaceId,
    string SubjectType,
    string SubjectId,
    string Resource,
    string Action,
    string Effect,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record UnifiedPermissionDecision(
    string WorkspaceId,
    string SubjectType,
    string SubjectId,
    string Resource,
    string Action,
    bool Allowed,
    string Reason,
    Guid? MatchedRuleId,
    string? MatchedEffect);

public sealed record UnifiedPermissionStatus(
    string Version,
    string WorkspaceId,
    int Rules,
    int ActiveRules,
    int AllowRules,
    int DenyRules,
    bool DefaultDeny,
    bool DenyOverridesAllow,
    bool WorkspaceScoped,
    bool ExpirySupported,
    bool ExplicitChangeConfirmationRequired,
    int MaximumRulesPerWorkspace,
    IReadOnlyList<string> SupportedSubjectTypes);

public sealed class UnifiedPermissionValidationException(string message)
    : Exception(message);
