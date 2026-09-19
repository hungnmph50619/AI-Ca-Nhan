using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ISystemCoreService
{
    Task<SystemHealthResponse> GetHealthAsync(
        CancellationToken cancellationToken = default);

    SystemCapabilitiesResponse GetCapabilities();
}

public sealed class SystemCoreService(
    IPersonalWorkspaceStore workspaceStore,
    IWorkspaceContextAccessor workspaceContext,
    IPersonalMemoryStore memoryStore,
    IKnowledgeDocumentStore knowledgeStore,
    IPersonalTaskStore taskStore,
    IWorkspaceFileService workspaceFiles,
    IToolRegistry toolRegistry,
    IAuditStore auditStore,
    IUndoService undoService,
    IComputerUseService computerUse,
    IBrowserAgentService browserAgent,
    IConnectorService connectors,
    IDevelopmentAgentService development,
    ICompanionService companion,
    ILifeContextService lifeContext,
    IAutomationStore automationStore,
    IHardeningStatusService hardening,
    IAgentFrameworkService agentFramework,
    IAiProviderResolver providerResolver,
    ILogger<SystemCoreService> logger) : ISystemCoreService
{
    private static readonly string[] StableModules =
    [
        "workspace",
        "memory",
        "documents",
        "rag",
        "tasks",
        "files",
        "context",
        "tools",
        "audit",
        "undo",
        "hardening",
        "conversations"
    ];

    private static readonly string[] ControlledModules =
    [
        "computer-use",
        "browser-agent",
        "connectors",
        "software-development",
        "android-companion",
        "life-context",
        "decision-engine",
        "automation",
        "agents"
    ];

    private static readonly string[] ReservedModules =
    [
        "policies"
    ];

    public async Task<SystemHealthResponse> GetHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var modules = new List<CoreModuleHealth>();

        modules.Add(Check(
            "workspace",
            () =>
            {
                var items = workspaceStore.GetAll();
                _ = workspaceContext.CurrentWorkspace;
                return new CoreModuleHealth(
                    "workspace",
                    CoreHealthStatuses.Healthy,
                    "Workspace contract và workspace hiện tại đọc được.",
                    items.Count);
            }));

        modules.Add(await CheckAsync(
            "memory",
            async () =>
            {
                var items = await memoryStore.GetAllAsync(cancellationToken);
                return new CoreModuleHealth(
                    "memory",
                    CoreHealthStatuses.Healthy,
                    "Memory store đọc được trong workspace hiện tại.",
                    items.Count);
            }));

        modules.Add(await CheckAsync(
            "documents",
            async () =>
            {
                var items = await knowledgeStore.GetAllAsync(cancellationToken);
                return new CoreModuleHealth(
                    "documents",
                    CoreHealthStatuses.Healthy,
                    "Knowledge store và metadata tài liệu đọc được.",
                    items.Count);
            }));

        modules.Add(Check(
            "tasks",
            () =>
            {
                var items = taskStore.GetAll();
                return new CoreModuleHealth(
                    "tasks",
                    CoreHealthStatuses.Healthy,
                    "Task store đọc được và recovery contract đã khởi tạo.",
                    items.Count);
            }));

        modules.Add(Check(
            "files",
            () =>
            {
                var listing = workspaceFiles.List(".", 1);
                return new CoreModuleHealth(
                    "files",
                    CoreHealthStatuses.Healthy,
                    "Filesystem sandbox của workspace truy cập được.",
                    listing.Entries.Count);
            }));

        modules.Add(Check(
            "tools",
            () =>
            {
                var tools = toolRegistry.GetAll();
                return new CoreModuleHealth(
                    "tools",
                    CoreHealthStatuses.Healthy,
                    "Tool registry hợp lệ và đã khởi tạo.",
                    tools.Count);
            }));

        modules.Add(Check(
            "audit",
            () =>
            {
                var summary = auditStore.GetSummary(
                    workspaceContext.CurrentWorkspaceId);
                return new CoreModuleHealth(
                    "audit",
                    CoreHealthStatuses.Healthy,
                    "Audit store đọc được trong workspace hiện tại.",
                    summary.TotalEvents);
            }));

        modules.Add(Check(
            "undo",
            () =>
            {
                var response = undoService.GetRecent(1);
                return new CoreModuleHealth(
                    "undo",
                    CoreHealthStatuses.Healthy,
                    "Undo store đọc được trong workspace hiện tại.",
                    response.Items.Count);
            }));

        modules.Add(Check(
            "hardening",
            () =>
            {
                var hardeningStatus = hardening.GetStatus();
                var moduleStatus = hardeningStatus.PermissionAudit.ViolationCount > 0
                    ? CoreHealthStatuses.Unavailable
                    : hardeningStatus.Status == HardeningStatuses.Healthy
                        ? CoreHealthStatuses.Healthy
                        : CoreHealthStatuses.Degraded;
                return new CoreModuleHealth(
                    "hardening",
                    moduleStatus,
                    hardeningStatus.PendingRestore
                        ? "Hardening đang chờ restore ở lần khởi động tiếp theo."
                        : hardeningStatus.Runtime.PreviousUncleanShutdown
                            ? "Hardening phát hiện lần chạy trước kết thúc không sạch; dữ liệu vẫn được kiểm tra trước khi tiếp tục."
                            : "Rate/resource guard, permission audit, crash marker và backup/restore foundation đang hoạt động.",
                    hardeningStatus.Backups.Count);
            },
            CoreHealthStatuses.Degraded));

        modules.Add(Check(
            "computer-use",
            () =>
            {
                var status = computerUse.GetStatus();
                if (!status.Supported)
                {
                    return new CoreModuleHealth(
                        "computer-use",
                        CoreHealthStatuses.Unconfigured,
                        "Computer Use v1.1 hiện chỉ hỗ trợ Windows.");
                }

                return status.InteractiveSession
                    ? new CoreModuleHealth(
                        "computer-use",
                        CoreHealthStatuses.Healthy,
                        "Controlled Computer Use khả dụng trong interactive Windows session.")
                    : new CoreModuleHealth(
                        "computer-use",
                        CoreHealthStatuses.Unconfigured,
                        "Windows hiện không chạy trong interactive session.");
            },
            CoreHealthStatuses.Degraded));

        modules.Add(Check(
            "browser-agent",
            () =>
            {
                var status = browserAgent.GetStatus();
                return status.Supported
                    ? new CoreModuleHealth(
                        "browser-agent",
                        CoreHealthStatuses.Healthy,
                        "Controlled Browser Agent HTTP/HTML khả dụng; private network và side-effect navigation bị chặn.")
                    : new CoreModuleHealth(
                        "browser-agent",
                        CoreHealthStatuses.Unconfigured,
                        "Browser Agent chưa khả dụng trong môi trường hiện tại.");
            },
            CoreHealthStatuses.Degraded));

        modules.Add(Check(
            "connectors",
            () =>
            {
                var status = connectors.GetStatus();
                var list = connectors.GetConnections();
                return status.Supported
                    ? new CoreModuleHealth(
                        "connectors",
                        CoreHealthStatuses.Healthy,
                        "Connector Foundation khả dụng; credential được mã hóa và write actions đang tắt.",
                        list.Connections.Count)
                    : new CoreModuleHealth(
                        "connectors",
                        CoreHealthStatuses.Unconfigured,
                        "Connector Foundation chưa khả dụng trong môi trường hiện tại.");
            },
            CoreHealthStatuses.Degraded));

        modules.Add(Check(
            "software-development",
            () =>
            {
                var status = development.GetStatus();
                var detail = status.DotnetAvailable || status.GitAvailable
                    ? "Software Development Agent khả dụng; shell/process tùy ý và Git write actions đang tắt."
                    : "Development Agent đã đăng ký nhưng git/dotnet chưa có trong PATH.";
                return new CoreModuleHealth(
                    "software-development",
                    status.DotnetAvailable || status.GitAvailable
                        ? CoreHealthStatuses.Healthy
                        : CoreHealthStatuses.Unconfigured,
                    detail);
            },
            CoreHealthStatuses.Degraded));

        modules.Add(Check(
            "android-companion",
            () =>
            {
                var status = companion.GetStatus();
                var devices = companion.GetDevices(
                    workspaceContext.CurrentWorkspaceId);
                return status.Enabled
                    ? new CoreModuleHealth(
                        "android-companion",
                        CoreHealthStatuses.Healthy,
                        status.SecureTransportRequired
                            ? "Android Companion khả dụng; client API yêu cầu HTTPS."
                            : "Android Companion khả dụng; HTTP không mã hóa đang được cho phép bằng cấu hình explicit.",
                        devices.Devices.Count)
                    : new CoreModuleHealth(
                        "android-companion",
                        CoreHealthStatuses.Unconfigured,
                        "Android Companion đang bị tắt trong cấu hình.");
            },
            CoreHealthStatuses.Degraded));

        modules.Add(await CheckAsync(
            "life-context",
            async () =>
            {
                var status = lifeContext.GetStatus();
                var sources = await lifeContext.GetSourcesAsync(
                    cancellationToken);

                return status.Supported
                    ? new CoreModuleHealth(
                        "life-context",
                        CoreHealthStatuses.Healthy,
                        status.AutomaticCollectionEnabled
                            ? "Life Context khả dụng; automatic collection đang bật."
                            : "Life Context khả dụng; explicit consent bắt buộc và automatic collection đang tắt.",
                        sources.Sources.Count)
                    : new CoreModuleHealth(
                        "life-context",
                        CoreHealthStatuses.Unconfigured,
                        "Life Context chưa khả dụng.");
            }));

        modules.Add(Check(
            "decision-engine",
            () =>
                new CoreModuleHealth(
                    "decision-engine",
                    CoreHealthStatuses.Healthy,
                    "Decision Engine khả dụng; chỉ phân tích/recommendation, không tool execution hoặc auto-action."),
            CoreHealthStatuses.Degraded));

        modules.Add(Check(
            "automation",
            () =>
            {
                var count = automationStore.Count(
                    workspaceContext.CurrentWorkspaceId);
                return new CoreModuleHealth(
                    "automation",
                    CoreHealthStatuses.Healthy,
                    "Automation scheduler khả dụng; chỉ auto-run READ/local step và không tự confirmation.",
                    count);
            },
            CoreHealthStatuses.Degraded));

        modules.Add(Check(
            "agents",
            () =>
            {
                var status = agentFramework.GetStatus();
                return status.RegisteredAgents > 0
                    ? new CoreModuleHealth(
                        "agents",
                        CoreHealthStatuses.Healthy,
                        "Agent Framework v2.1 khả dụng; explicit invocation bắt buộc, tool execution/delegation/messaging vẫn tắt.",
                        status.RegisteredAgents)
                    : new CoreModuleHealth(
                        "agents",
                        CoreHealthStatuses.Unavailable,
                        "Agent Framework chưa có agent nào được đăng ký.");
            }));

        modules.Add(Check(
            "ai-provider",
            () =>
            {
                var provider = providerResolver.GetActive();
                return provider.IsConfigured
                    ? new CoreModuleHealth(
                        "ai-provider",
                        CoreHealthStatuses.Healthy,
                        $"Nhà cung cấp {provider.Name} đã được cấu hình.")
                    : new CoreModuleHealth(
                        "ai-provider",
                        CoreHealthStatuses.Unconfigured,
                        $"Nhà cung cấp {provider.Name} chưa có cấu hình đầy đủ.");
            },
            CoreHealthStatuses.Degraded));

        var localModules = modules
            .Where(module =>
                module.Module != "ai-provider"
                && module.Module != "computer-use"
                && module.Module != "browser-agent"
                && module.Module != "connectors"
                && module.Module != "software-development"
                && module.Module != "android-companion"
                && module.Module != "life-context"
                && module.Module != "decision-engine"
                && module.Module != "automation")
            .ToArray();
        var ready = localModules.All(
            module => module.Status != CoreHealthStatuses.Unavailable);
        var degraded = modules.Any(
            module => module.Status is CoreHealthStatuses.Degraded
                or CoreHealthStatuses.Unconfigured);
        var status = !ready
            ? CoreHealthStatuses.Unavailable
            : degraded
                ? CoreHealthStatuses.Degraded
                : CoreHealthStatuses.Healthy;

        return new SystemHealthResponse(
            PersonalAiRelease.Version,
            PersonalAiRelease.ApiContractVersion,
            PersonalAiRelease.Channel,
            ready,
            status,
            workspaceContext.CurrentWorkspaceId,
            DateTimeOffset.UtcNow,
            modules);
    }

    public SystemCapabilitiesResponse GetCapabilities() =>
        new(
            PersonalAiRelease.Version,
            PersonalAiRelease.ApiContractVersion,
            PersonalAiRelease.Channel,
            StableModules,
            ControlledModules,
            ReservedModules,
            new CoreSafetyContract(
                WorkspaceIsolation: true,
                WriteRequiresConfirmation: true,
                DeleteRequiresConfirmation: true,
                ExternalRequiresConfirmation: true,
                UndoRequiresConfirmation: true,
                ComputerControlRequiresConfirmation: true,
                SensitiveComputerObservationRequiresConfirmation: true,
                BrowserUseRequiresConfirmation: true,
                BrowserPrivateNetworkAccess: false,
                BrowserSideEffectsEnabled: false,
                ConnectorUseRequiresConfirmation: true,
                ConnectorSecretsEncrypted: true,
                ConnectorWriteActionsEnabled: false,
                DevelopmentUseRequiresConfirmation: true,
                DevelopmentArbitraryShellEnabled: false,
                DevelopmentGitWriteActionsEnabled: false,
                DevelopmentArbitraryProcessEnabled: false,
                CompanionPairingRequired: true,
                CompanionDeviceTokensHashed: true,
                CompanionRemoteToolExecutionEnabled: false,
                CompanionRemoteTaskMutationEnabled: false,
                LifeContextExplicitConsentRequired: true,
                LifeContextAutomaticCollectionEnabled: false,
                LifeContextContentEncrypted: true,
                DecisionEngineRequiresUserDecision: true,
                DecisionEngineAutoActionEnabled: false,
                DecisionEngineToolExecutionEnabled: false,
                DecisionEnginePersistsAnalyses: false,
                AutomationRequiresExplicitCreation: true,
                AutomationAutoConfirmationEnabled: false,
                AutomationDecisionRecommendationAutoExecutionEnabled: false,
                AutomationRunsAtMostOneTaskStepPerTick: true,
                AutomaticMultiStepExecution: false,
                BackgroundScheduler: true,
                AutonomousAgentLoop: false,
                ParallelToolCalls: false),
            new CoreLimitsContract(
                PersonalWorkspaceStore.MaximumWorkspaces,
                SqlitePersonalTaskStore.MaximumTasks,
                ContextManagerService.MaximumContextCharacters,
                WorkspaceFileService.MaximumFileBytes,
                SqliteAuditStore.RetentionDays,
                SqliteAuditStore.MaximumEntries,
                SqliteUndoStore.AvailabilityDays,
                SqliteUndoStore.MaximumEntries,
                SqliteUndoStore.MaximumSnapshotBytes,
                BrowserAgentService.MaximumResponseBytes,
                BrowserAgentService.MaximumPageTextCharacters,
                BrowserAgentService.MaximumLinks,
                BrowserAgentService.MaximumRedirects,
                ConnectorService.MaximumConnectionsPerWorkspace,
                ConnectorService.MaximumResponseBytes,
                ConnectorService.MaximumReturnedCharacters,
                ConnectorService.MaximumRedirects,
                DevelopmentAgentService.MaximumScannedFiles,
                DevelopmentAgentService.MaximumSearchHits,
                DevelopmentAgentService.MaximumProcessOutputCharacters,
                CompanionService.MaximumDevicesPerWorkspace,
                CompanionService.PairingLifetimeMinutes,
                LifeContextService.MaximumSourcesPerWorkspace,
                LifeContextService.MaximumEntriesPerWorkspace,
                LifeContextService.MaximumContentCharacters,
                LifeContextService.MaximumRetentionDays,
                DecisionEngineService.MaximumQuestionCharacters,
                DecisionEngineService.MaximumOptions,
                DecisionEngineService.MaximumCriteria,
                SqliteAutomationStore.MaximumAutomationsPerWorkspace,
                AutomationService.MinimumIntervalMinutes,
                AutomationService.MaximumIntervalMinutes,
                AutomationService.SchedulerPollSeconds),
            WorkspaceEndpoints.WorkspaceHeaderName,
            SystemHardeningMiddleware.RequestIdHeaderName);

    private CoreModuleHealth Check(
        string module,
        Func<CoreModuleHealth> action,
        string failureStatus = CoreHealthStatuses.Unavailable)
    {
        try
        {
            return action();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Core health check failed for module {Module}.",
                module);
            return new CoreModuleHealth(
                module,
                failureStatus,
                "Module không vượt qua kiểm tra readiness cục bộ.");
        }
    }

    private async Task<CoreModuleHealth> CheckAsync(
        string module,
        Func<Task<CoreModuleHealth>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Core health check failed for module {Module}.",
                module);
            return new CoreModuleHealth(
                module,
                CoreHealthStatuses.Unavailable,
                "Module không vượt qua kiểm tra readiness cục bộ.");
        }
    }
}
