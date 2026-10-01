namespace PersonalAI.Web.Models;

public sealed record DatabaseMigrationComponentStatus(
    string Component,
    int ExpectedMigrations,
    int AppliedMigrations,
    bool UpToDate,
    IReadOnlyList<string> AppliedIds);

public sealed record DatabaseUpgradeStatus(
    string Version,
    string WorkspaceId,
    bool UpToDate,
    bool StartupMigrationEnabled,
    bool ChecksumValidationEnabled,
    bool AtomicMigrationEnabled,
    bool FailClosed,
    int RegisteredComponents,
    int ExpectedMigrations,
    int AppliedMigrations,
    IReadOnlyList<DatabaseMigrationComponentStatus> Components);
