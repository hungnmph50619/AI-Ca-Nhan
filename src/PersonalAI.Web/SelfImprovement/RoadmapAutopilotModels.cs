namespace PersonalAI.Web.SelfImprovement;

public sealed record RoadmapVersionSpec(
    string Version,
    string Name,
    string Goal,
    IReadOnlyList<string> SearchTerms,
    IReadOnlyList<string> AcceptanceChecks);

public sealed record RoadmapAutopilotStatus(
    string Version,
    string WorkspaceId,
    string CurrentProductVersion,
    string? NextVersion,
    string? NextVersionName,
    bool OpenAiConfigured,
    string OpenAiModel,
    bool GitAvailable,
    bool DotnetAvailable,
    bool AutomaticMergeEnabled,
    bool AutomaticPushEnabled,
    int MaximumRepairAttempts);

public sealed record RunRoadmapAutopilotRequest(
    string RepositoryPath = ".",
    string DotnetTargetPath = "PersonalAI.sln",
    string BaseBranch = "main",
    bool ConfirmExternalAi = false,
    bool ConfirmBranchCreation = false,
    bool ConfirmFileChanges = false,
    bool RunTests = true,
    int? MaximumRepairAttempts = null);

public sealed record RoadmapAutopilotFileChange(
    string Path,
    string Mode,
    string Sha256);

public sealed record RoadmapAutopilotVerification(
    bool RestorePassed,
    bool BuildPassed,
    bool TestsPassed,
    string RestoreSummary,
    string BuildSummary,
    string TestSummary);

public sealed record RoadmapAutopilotRunResult(
    string Version,
    string WorkspaceId,
    string TargetVersion,
    string TargetName,
    string Branch,
    string Provider,
    string Model,
    int Attempts,
    IReadOnlyList<RoadmapAutopilotFileChange> FilesChanged,
    RoadmapAutopilotVerification Verification,
    string Status,
    bool ReadyForCommit,
    bool Pushed,
    bool Merged,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public static class RoadmapAutopilotStatuses
{
    public const string ReadyForCommit = "ready-for-commit";
    public const string ChangesRequired = "changes-required";
}

public sealed class RoadmapAutopilotValidationException(string message)
    : Exception(message);
