using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IUnifiedPermissionService
{
    UnifiedPermissionStatus GetStatus();
    IReadOnlyList<UnifiedPermissionRule> GetAll();
    UnifiedPermissionRule Set(SetUnifiedPermissionRequest request);
    bool Revoke(Guid ruleId, RevokeUnifiedPermissionRequest request);
    UnifiedPermissionDecision Evaluate(EvaluateUnifiedPermissionRequest request);
}

public sealed class UnifiedPermissionService(
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IUnifiedPermissionService
{
    public const int MaximumRulesPerWorkspace = 2_000;
    public const int MaximumSubjectIdCharacters = 180;
    public const int MaximumResourceCharacters = 180;
    public const int MaximumActionCharacters = 80;

    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public UnifiedPermissionStatus GetStatus()
    {
        var all = GetAll();
        var now = DateTimeOffset.UtcNow;
        var active = all.Where(x => x.ExpiresAt is null || x.ExpiresAt > now).ToArray();

        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            active.Length,
            active.Count(x => x.Effect == UnifiedPermissionEffects.Allow),
            active.Count(x => x.Effect == UnifiedPermissionEffects.Deny),
            DefaultDeny: true,
            DenyOverridesAllow: true,
            WorkspaceScoped: true,
            ExpirySupported: true,
            ExplicitChangeConfirmationRequired: true,
            MaximumRulesPerWorkspace,
            UnifiedPermissionSubjectTypes.All.Order(StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyList<UnifiedPermissionRule> GetAll()
    {
        lock (_gate)
            return Load()
                .OrderBy(x => x.SubjectType, StringComparer.Ordinal)
                .ThenBy(x => x.SubjectId, StringComparer.Ordinal)
                .ThenBy(x => x.Resource, StringComparer.Ordinal)
                .ThenBy(x => x.Action, StringComparer.Ordinal)
                .ToArray();
    }

    public UnifiedPermissionRule Set(SetUnifiedPermissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmChange)
            throw new UnifiedPermissionValidationException(
                "Cần ConfirmChange=true trước khi thay đổi quyền.");

        var subjectType = NormalizeSubjectType(request.SubjectType);
        var subjectId = NormalizeSubjectId(request.SubjectId);
        var resource = NormalizeResource(request.Resource);
        var action = NormalizeAction(request.Action);
        var effect = NormalizeEffect(request.Effect);

        if (request.ExpiresAt is not null &&
            request.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new UnifiedPermissionValidationException(
                "ExpiresAt phải nằm trong tương lai.");
        }

        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x =>
                x.SubjectType == subjectType &&
                x.SubjectId == subjectId &&
                x.Resource == resource &&
                x.Action == action);

            var now = DateTimeOffset.UtcNow;
            UnifiedPermissionRule rule;

            if (index >= 0)
            {
                var current = all[index];
                rule = current with
                {
                    Effect = effect,
                    UpdatedAt = now,
                    ExpiresAt = request.ExpiresAt
                };
                all[index] = rule;
            }
            else
            {
                if (all.Count >= MaximumRulesPerWorkspace)
                    throw new UnifiedPermissionValidationException(
                        $"Workspace đã đạt giới hạn {MaximumRulesPerWorkspace} permission rules.");

                rule = new(
                    Guid.NewGuid(),
                    workspace.CurrentWorkspaceId,
                    subjectType,
                    subjectId,
                    resource,
                    action,
                    effect,
                    now,
                    now,
                    request.ExpiresAt);
                all.Add(rule);
            }

            Save(all);

            audit.Record(
                AuditAgents.User,
                "permission.rule.set",
                $"permission:{rule.Id:D}",
                $"subject:{subjectType}:{subjectId};resource:{resource};action:{action};effect:{effect};expires:{request.ExpiresAt?.ToString("O") ?? "none"}",
                AuditResults.Succeeded,
                workspaceId: workspace.CurrentWorkspaceId);

            return rule;
        }
    }

    public bool Revoke(Guid ruleId, RevokeUnifiedPermissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmRevoke)
            throw new UnifiedPermissionValidationException(
                "Cần ConfirmRevoke=true trước khi thu hồi permission rule.");

        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x => x.Id == ruleId);
            if (index < 0)
                return false;

            var rule = all[index];
            all.RemoveAt(index);
            Save(all);

            audit.Record(
                AuditAgents.User,
                "permission.rule.revoke",
                $"permission:{rule.Id:D}",
                $"subject:{rule.SubjectType}:{rule.SubjectId};resource:{rule.Resource};action:{rule.Action}",
                AuditResults.Succeeded,
                workspaceId: workspace.CurrentWorkspaceId);

            return true;
        }
    }

    public UnifiedPermissionDecision Evaluate(
        EvaluateUnifiedPermissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var subjectType = NormalizeSubjectType(request.SubjectType);
        var subjectId = NormalizeSubjectId(request.SubjectId);
        var resource = NormalizeResource(request.Resource);
        var action = NormalizeAction(request.Action);
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            var matches = Load()
                .Where(x =>
                    x.SubjectType == subjectType &&
                    x.SubjectId == subjectId &&
                    (x.Resource == resource || x.Resource == "*") &&
                    (x.Action == action || x.Action == "*") &&
                    (x.ExpiresAt is null || x.ExpiresAt > now))
                .OrderByDescending(x => Specificity(x, resource, action))
                .ThenByDescending(x => x.UpdatedAt)
                .ToArray();

            var deny = matches.FirstOrDefault(x =>
                x.Effect == UnifiedPermissionEffects.Deny);
            if (deny is not null)
            {
                return new(
                    workspace.CurrentWorkspaceId,
                    subjectType,
                    subjectId,
                    resource,
                    action,
                    Allowed: false,
                    Reason: "explicit-deny",
                    deny.Id,
                    deny.Effect);
            }

            var allow = matches.FirstOrDefault(x =>
                x.Effect == UnifiedPermissionEffects.Allow);
            if (allow is not null)
            {
                return new(
                    workspace.CurrentWorkspaceId,
                    subjectType,
                    subjectId,
                    resource,
                    action,
                    Allowed: true,
                    Reason: "explicit-allow",
                    allow.Id,
                    allow.Effect);
            }

            return new(
                workspace.CurrentWorkspaceId,
                subjectType,
                subjectId,
                resource,
                action,
                Allowed: false,
                Reason: "default-deny",
                MatchedRuleId: null,
                MatchedEffect: null);
        }
    }

    private static int Specificity(
        UnifiedPermissionRule rule,
        string resource,
        string action)
    {
        var score = 0;
        if (rule.Resource == resource) score += 2;
        if (rule.Action == action) score += 1;
        return score;
    }

    private static string NormalizeSubjectType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (!UnifiedPermissionSubjectTypes.All.Contains(normalized))
            throw new UnifiedPermissionValidationException(
                "SubjectType phải là user, device, companion, agent hoặc system.");
        return normalized;
    }

    private static string NormalizeSubjectId(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > MaximumSubjectIdCharacters)
            throw new UnifiedPermissionValidationException(
                $"SubjectId phải có từ 1 đến {MaximumSubjectIdCharacters} ký tự.");
        return normalized;
    }

    private static string NormalizeResource(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized == "*")
            return normalized;

        if (normalized.Length is < 1 or > MaximumResourceCharacters)
            throw new UnifiedPermissionValidationException(
                $"Resource phải có từ 1 đến {MaximumResourceCharacters} ký tự hoặc '*'.");
        return normalized;
    }

    private static string NormalizeAction(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized == "*")
            return normalized;

        if (normalized.Length is < 1 or > MaximumActionCharacters)
            throw new UnifiedPermissionValidationException(
                $"Action phải có từ 1 đến {MaximumActionCharacters} ký tự hoặc '*'.");
        return normalized;
    }

    private static string NormalizeEffect(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (!UnifiedPermissionEffects.All.Contains(normalized))
            throw new UnifiedPermissionValidationException(
                "Effect phải là allow hoặc deny.");
        return normalized;
    }

    private List<UnifiedPermissionRule> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<UnifiedPermissionRule>>(
                File.ReadAllText(path),
                JsonOptions) ?? [];
        }
        catch (JsonException exception)
        {
            throw new UnifiedPermissionValidationException(
                $"Permission state bị hỏng và fail-closed: {exception.Message}");
        }
    }

    private void Save(IReadOnlyList<UnifiedPermissionRule> rules)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(rules, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private string PathForWorkspace()
    {
        var workspaceId = workspace.CurrentWorkspaceId;
        var safe = string.Concat(workspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_root, $"permissions-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var configured = configuration["UnifiedPermission:Root"];
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".personalai");
        }

        return Path.Combine(localData, "PersonalAI", "Permissions");
    }
}
