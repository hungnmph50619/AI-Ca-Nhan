using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDatabaseUpgradeService
{
    DatabaseUpgradeStatus EnsureUpToDate();
    DatabaseUpgradeStatus GetStatus();
}

public sealed class DatabaseUpgradeService(
    IAuditStore auditStore,
    IUndoService undo,
    IWorkspaceContextAccessor workspace) : IDatabaseUpgradeService
{
    public DatabaseUpgradeStatus EnsureUpToDate()
    {
        _ = auditStore.GetSummary(
            workspace.CurrentWorkspaceId);
        _ = undo.GetRecent(1);
        return BuildStatus();
    }

    public DatabaseUpgradeStatus GetStatus() =>
        BuildStatus();

    private DatabaseUpgradeStatus BuildStatus()
    {
        var states = SqliteSchemaMigrationEngine.Snapshot();
        var components = states
            .Select(x => new DatabaseMigrationComponentStatus(
                x.Component,
                x.ExpectedMigrations,
                x.AppliedMigrations,
                x.UpToDate,
                x.AppliedIds))
            .ToArray();

        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            components.Length >= 2
                && components.All(x => x.UpToDate),
            StartupMigrationEnabled: true,
            ChecksumValidationEnabled: true,
            AtomicMigrationEnabled: true,
            FailClosed: true,
            components.Length,
            components.Sum(x => x.ExpectedMigrations),
            components.Sum(x => x.AppliedMigrations),
            components);
    }
}

public sealed class DatabaseUpgradeHostedService(
    IDatabaseUpgradeService upgrades,
    ILogger<DatabaseUpgradeHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var status = upgrades.EnsureUpToDate();
        if (!status.UpToDate)
        {
            throw new InvalidOperationException(
                "Database migrations chưa ở trạng thái up-to-date.");
        }

        logger.LogInformation(
            "Database upgrades ready. Components={Components} Applied={Applied}/{Expected}",
            status.RegisteredComponents,
            status.AppliedMigrations,
            status.ExpectedMigrations);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
