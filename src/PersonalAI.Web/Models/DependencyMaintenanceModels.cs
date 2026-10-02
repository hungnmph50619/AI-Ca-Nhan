namespace PersonalAI.Web.Models;

public static class DependencyUpgradeKinds
{
    public const string Patch = "patch";
    public const string Minor = "minor";
    public const string Major = "major";
    public const string Same = "same";
}

public static class DependencyMaintenanceStates
{
    public const string Planned = "planned";
    public const string Applied = "applied";
    public const string BlockedMajor = "blocked-major";
}

public sealed record PlanDependencyMaintenanceRequest(
    Guid DevelopmentRunId,
    string ProjectPath,
    string PackageId,
    string TargetVersion,
    string StartPoint = "main",
    bool ConfirmCreateWorktree = false);

public sealed record ApplyDependencyMaintenanceRequest(
    Guid PlanId,
    bool ConfirmApply = false,
    bool ConfirmMajorUpgrade = false);

public sealed record DependencyMaintenancePlan(
    Guid Id,
    string WorkspaceId,
    Guid DevelopmentRunId,
    string WorktreePath,
    string ProjectPath,
    string PackageId,
    string CurrentVersion,
    string TargetVersion,
    string UpgradeKind,
    string RiskLevel,
    bool AutomaticEligible,
    bool RequiresUserApproval,
    string State,
    string? ResultSha256,
    IReadOnlyList<string> RequiredPostApplyGates,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DependencyMaintenanceStatus(
    string Version,
    string WorkspaceId,
    int Plans,
    int AppliedPlans,
    int BlockedMajorPlans,
    bool MajorAutoUpgradeAllowed,
    bool WorktreeOnly,
    bool ShaGuardEnabled,
    IReadOnlyList<string> RequiredPostApplyGates);

public sealed class DependencyMaintenanceValidationException(string message)
    : Exception(message);
