using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IPersonalAiOsService
{
    Task<PersonalAiOsStatusResponse> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task<PersonalAiOsManifestResponse> GetManifestAsync(
        CancellationToken cancellationToken = default);
}

public sealed class PersonalAiOsService(
    ISystemCoreService core,
    IWorkspaceContextAccessor workspaceContext,
    IAutonomousDevelopmentService autonomousDevelopment,
    INightlyImprovementService nightlyImprovement,
    IUnifiedPermissionService permissions,
    IAuditStore auditStore,
    IUndoService undo,
    ISystemResourceLimitService resourceLimits,
    ILoopGuardService loopGuard,
    IEmergencyStopService emergencyStop,
    IDisasterRecoveryService disasterRecovery,
    IDatabaseUpgradeService databaseUpgrades) : IPersonalAiOsService
{
    public const string Edition = "Personal AI OS";
    public const string Stage = "v3.0";
    public const string NextStage = "continuous-stable";

    private static readonly PersonalAiOsLayer[] Layers =
    [
        new(
            "knowledge",
            "Knowledge & Context",
            "Dữ liệu và ngữ cảnh mà PersonalAI dùng để hiểu người dùng và câu hỏi hiện tại.",
            ["memory", "knowledge", "life-context"]),
        new(
            "reasoning",
            "Planning & Decision",
            "Lập kế hoạch, quản lý công việc và hỗ trợ quyết định mà không tự thay người dùng quyết định.",
            ["tasks", "decision-engine"]),
        new(
            "execution",
            "Tools & Automation",
            "Thực thi công cụ và automation dưới permission, confirmation và giới hạn tài nguyên.",
            ["tools", "files", "automation", "software-development"]),
        new(
            "interfaces",
            "Device & External Interfaces",
            "Các bề mặt tương tác với máy tính, web, tài khoản ngoài và thiết bị Android.",
            ["desktop", "browser", "connectors", "email", "calendar", "android"]),
        new(
            "coordination",
            "Agent Framework",
            "Agent Framework có tám agent: Personal Assistant, Planner, Research, Developer, Office, Operator, Reviewer và Security; Security rà soát mẫu rủi ro cục bộ, không thực thi hay chặn thao tác.",
            ["agent-framework"]),
        new(
            "governance",
            "Governance & Recovery",
            "Audit, undo, hardening, backup và recovery bảo vệ toàn bộ hệ thống.",
            ["audit", "undo", "hardening", "continuous-improvement"])
    ];

    private static readonly string[] Boundaries =
    [
        "v3.0 không bật continuous self-improvement vô điều kiện; nightly scheduler vẫn cấu hình explicit và mặc định tắt.",
        "Autonomous development chỉ khép eligible low-risk path và không được bypass permission, review, security, benchmark, CI, merge-policy hoặc local-sync gates.",
        "External AI và GitHub side effects vẫn dùng confirmation/credential/policy gates tương ứng; v3.0 không tự cấp quyền mới.",
        "Emergency stop có ưu tiên chặn execution mới; audit/system log vẫn được giữ để review trước explicit release.",
        "Loop guard, resource limits, database migration fail-closed, disaster recovery và undo/revalidation vẫn là ranh giới bắt buộc.",
        "Decision Engine chỉ phân tích và recommendation; người dùng vẫn là người ra quyết định cuối cùng cho các lựa chọn không thuộc low-risk automatic policy.",
        "Email/Calendar provider-specific write capability không được suy diễn chỉ vì connector/life-context foundation tồn tại.",
        "Parallel tool calls và general autonomous agent loop không tự động được mở bởi continuous development controller."
    ];

    public async Task<PersonalAiOsStatusResponse> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var health = await core.GetHealthAsync(cancellationToken);
        var baseCapabilities = BuildCapabilities(health.Modules);
        var workspace = workspaceContext.CurrentWorkspace;
        var continuous = await BuildContinuousImprovementAsync(
            health,
            cancellationToken);
        var capabilities = AppendContinuousImprovementCapability(
            baseCapabilities,
            continuous);
        var readiness = BuildReadiness(
            health,
            capabilities,
            continuous);

        return new PersonalAiOsStatusResponse(
            PersonalAiRelease.Version,
            PersonalAiRelease.ApiContractVersion,
            Edition,
            Stage,
            workspace.Id,
            workspace.Name,
            DateTimeOffset.UtcNow,
            readiness,
            BuildGovernance(),
            Layers,
            capabilities,
            NextStage,
            continuous);
    }

    public async Task<PersonalAiOsManifestResponse> GetManifestAsync(
        CancellationToken cancellationToken = default)
    {
        var health = await core.GetHealthAsync(cancellationToken);
        var capabilities = BuildCapabilities(health.Modules);
        var continuous = await BuildContinuousImprovementAsync(
            health,
            cancellationToken);

        return new PersonalAiOsManifestResponse(
            PersonalAiRelease.Version,
            Edition,
            Stage,
            Layers,
            AppendContinuousImprovementCapability(
                capabilities,
                continuous),
            BuildGovernance(),
            Boundaries,
            continuous);
    }

    private async Task<PersonalAiOsContinuousImprovement>
        BuildContinuousImprovementAsync(
            SystemHealthResponse health,
            CancellationToken cancellationToken)
    {
        var workspaceId = workspaceContext.CurrentWorkspaceId;
        var autonomous = autonomousDevelopment.GetStatus();
        var nightly = nightlyImprovement.GetStatus();
        var permissionStatus = permissions.GetStatus();
        var undoStatus = undo.GetStatus();
        var resourceStatus = resourceLimits.GetStatus();
        var loopStatus = loopGuard.GetStatus(workspaceId);
        var stopStatus = emergencyStop.GetStatus();
        var recoveryStatus = await disasterRecovery.GetStatusAsync(
            cancellationToken);
        var databaseStatus = databaseUpgrades.GetStatus();

        _ = auditStore.GetSummary(workspaceId);

        var auditHealth = health.Modules.FirstOrDefault(x =>
            x.Module.Equals("audit", StringComparison.OrdinalIgnoreCase));
        var systemLogReady = auditHealth is not null
            && auditHealth.Status != CoreHealthStatuses.Unavailable;

        var autonomousReady =
            autonomous.FullPipelineControllerEnabled
            && !autonomous.PolicyBypassAllowed
            && !autonomous.DirectMainPushAllowed
            && !autonomous.HighRiskAutoMergeAllowed;

        var permissionReady =
            permissionStatus.DefaultDeny
            && permissionStatus.DenyOverridesAllow
            && permissionStatus.WorkspaceScoped
            && permissionStatus.ExplicitChangeConfirmationRequired;

        var undoReady =
            undoStatus.ExplicitConfirmationRequired
            && undoStatus.PreconditionRevalidationRequired
            && undoStatus.WorkspaceScoped
            && undoStatus.AuditTrailEnabled;

        var resourceReady =
            resourceStatus.RequestRateLimitEnforced
            && resourceStatus.ConcurrentRequestLimitEnforced
            && resourceStatus.RequestBodyLimitEnforced
            && resourceStatus.RateWindowMemoryLimitEnforced
            && resourceStatus.FailClosed;

        var loopReady =
            loopStatus.DuplicateFingerprintDetectionEnabled
            && loopStatus.RunawayCostDetectionEnabled
            && loopStatus.BranchExplosionGuardEnabled
            && loopStatus.DuplicateTaskGuardEnabled
            && loopStatus.StopReasonAudited
            && loopStatus.FailClosed;

        var stopReady =
            stopStatus.AuditPreserved
            && stopStatus.ExplicitReleaseRequired;

        var recoveryReady =
            recoveryStatus.ArchiveIntegrityVerificationEnabled
            && recoveryStatus.PathTraversalProtectionEnabled
            && recoveryStatus.RestoreSizeLimitsEnforced
            && recoveryStatus.PreRestoreBackupEnabled
            && recoveryStatus.RollbackOnRestoreFailureEnabled
            && recoveryStatus.ExplicitConfirmationRequired;

        var databaseReady =
            databaseStatus.UpToDate
            && databaseStatus.StartupMigrationEnabled
            && databaseStatus.ChecksumValidationEnabled
            && databaseStatus.AtomicMigrationEnabled
            && databaseStatus.FailClosed;

        var blockers = new List<string>();
        AddBlocker(!autonomousReady,
            "Autonomous development controller chưa đạt safety contract.",
            blockers);
        AddBlocker(!permissionReady,
            "Unified permission system chưa đạt default-deny contract.",
            blockers);
        AddBlocker(!systemLogReady,
            "System log/audit chưa sẵn sàng.",
            blockers);
        AddBlocker(!undoReady,
            "Undo/revalidation chưa sẵn sàng.",
            blockers);
        AddBlocker(!resourceReady,
            "Resource limits chưa fail-closed.",
            blockers);
        AddBlocker(!loopReady,
            "Loop guard chưa sẵn sàng.",
            blockers);
        AddBlocker(!stopReady,
            "Emergency stop contract chưa sẵn sàng.",
            blockers);
        AddBlocker(stopStatus.Engaged,
            "Emergency stop đang engaged.",
            blockers);
        AddBlocker(!recoveryReady,
            "Disaster recovery contract chưa sẵn sàng.",
            blockers);
        AddBlocker(!databaseReady,
            "Database migrations chưa up-to-date.",
            blockers);
        AddBlocker(!nightly.LowRiskOnly
            || !nightly.DownstreamPullRequestUsesPolicy
            || !nightly.DownstreamAutoMergeUsesMergePolicy,
            "Nightly improvement policy boundary chưa sẵn sàng.",
            blockers);

        return new(
            Available: blockers.Count == 0,
            EnabledByDefault: false,
            AutonomousDevelopmentControllerReady: autonomousReady,
            NightlySchedulerAvailable: true,
            NightlySchedulerEnabled: nightly.SchedulerEnabled,
            LowRiskOnly: nightly.LowRiskOnly,
            PolicyBypassAllowed: autonomous.PolicyBypassAllowed,
            DirectMainPushAllowed: autonomous.DirectMainPushAllowed,
            HighRiskAutoMergeAllowed: autonomous.HighRiskAutoMergeAllowed,
            UnifiedPermissionsReady: permissionReady,
            SystemLogReady: systemLogReady,
            UndoReady: undoReady,
            ResourceLimitsReady: resourceReady,
            LoopGuardReady: loopReady,
            EmergencyStopReady: stopReady,
            DisasterRecoveryReady: recoveryReady,
            DatabaseUpgradesReady: databaseReady,
            AcceptanceBaseline:
                "v2.9.9/full-autonomous-development-test-v299.yml",
            Blockers: blockers);
    }

    private static IReadOnlyList<PersonalAiOsCapability>
        AppendContinuousImprovementCapability(
            IReadOnlyList<PersonalAiOsCapability> capabilities,
            PersonalAiOsContinuousImprovement continuous)
    {
        return
        [
            .. capabilities,
            new(
                "continuous-improvement",
                "Continuous Self-Improvement",
                "governance",
                continuous.Available
                    ? PersonalAiOsCapabilityStates.Controlled
                    : PersonalAiOsCapabilityStates.Unavailable,
                continuous.Available
                    ? "Low-risk autonomous development controller sẵn sàng dưới permission, audit, policy, rollback, resource/loop guards, disaster recovery và emergency stop."
                    : $"Continuous improvement chưa sẵn sàng: {string.Join("; ", continuous.Blockers)}",
                [
                    "/api/development/autonomous/status",
                    "/api/development/nightly/status",
                    "/api/system/permissions/status",
                    "/api/system/logs/status",
                    "/api/system/emergency-stop/status"
                ],
                true,
                true,
                true)
        ];
    }

    private static void AddBlocker(
        bool condition,
        string message,
        ICollection<string> blockers)
    {
        if (condition)
            blockers.Add(message);
    }

    private static PersonalAiOsReadiness BuildReadiness(
        SystemHealthResponse health,
        IReadOnlyList<PersonalAiOsCapability> capabilities,
        PersonalAiOsContinuousImprovement? continuous = null)
    {
        var blockers = new List<string>();

        if (continuous is not null && !continuous.Available)
        {
            blockers.Add(
                "Continuous self-improvement safety plane chưa sẵn sàng.");
        }

        RequireAvailable(
            health.Modules,
            "workspace",
            "Workspace boundary chưa sẵn sàng.",
            blockers);
        RequireAvailable(
            health.Modules,
            "tasks",
            "Task Engine chưa sẵn sàng.",
            blockers);
        RequireAvailable(
            health.Modules,
            "tools",
            "Tool Framework chưa sẵn sàng.",
            blockers);
        RequireAvailable(
            health.Modules,
            "audit",
            "Audit Foundation chưa sẵn sàng.",
            blockers);
        RequireAvailable(
            health.Modules,
            "hardening",
            "Hardening đang unavailable; chưa nên mở nền Multi-Agent.",
            blockers);
        RequireAvailable(
            health.Modules,
            "agents",
            "Agent Framework chưa sẵn sàng.",
            blockers);

        var osContractReady = health.Ready
            && blockers.Count == 0;

        return new PersonalAiOsReadiness(
            osContractReady,
            ReadyForV21Foundation: osContractReady,
            health.Status,
            capabilities.Count,
            capabilities.Count(item =>
                item.State == PersonalAiOsCapabilityStates.Ready),
            capabilities.Count(item =>
                item.State == PersonalAiOsCapabilityStates.Controlled),
            capabilities.Count(item =>
                item.State == PersonalAiOsCapabilityStates.Foundation),
            capabilities.Count(item =>
                item.State == PersonalAiOsCapabilityStates.Unconfigured),
            capabilities.Count(item =>
                item.State == PersonalAiOsCapabilityStates.Unavailable),
            blockers);
    }

    private static void RequireAvailable(
        IReadOnlyList<CoreModuleHealth> modules,
        string module,
        string message,
        ICollection<string> blockers)
    {
        var item = modules.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Module,
                module,
                StringComparison.OrdinalIgnoreCase));

        if (item is null
            || item.Status == CoreHealthStatuses.Unavailable)
        {
            blockers.Add(message);
        }
    }

    private static IReadOnlyList<PersonalAiOsCapability> BuildCapabilities(
        IReadOnlyList<CoreModuleHealth> modules)
    {
        string Health(string module) =>
            modules.FirstOrDefault(item =>
                string.Equals(
                    item.Module,
                    module,
                    StringComparison.OrdinalIgnoreCase))
                ?.Status
            ?? CoreHealthStatuses.Unavailable;

        string StableState(string module) =>
            Health(module) switch
            {
                CoreHealthStatuses.Healthy => PersonalAiOsCapabilityStates.Ready,
                CoreHealthStatuses.Degraded => PersonalAiOsCapabilityStates.Controlled,
                CoreHealthStatuses.Unconfigured => PersonalAiOsCapabilityStates.Unconfigured,
                _ => PersonalAiOsCapabilityStates.Unavailable
            };

        string ControlledState(string module) =>
            Health(module) switch
            {
                CoreHealthStatuses.Healthy => PersonalAiOsCapabilityStates.Controlled,
                CoreHealthStatuses.Degraded => PersonalAiOsCapabilityStates.Controlled,
                CoreHealthStatuses.Unconfigured => PersonalAiOsCapabilityStates.Unconfigured,
                _ => PersonalAiOsCapabilityStates.Unavailable
            };

        string FoundationState(string module) =>
            Health(module) == CoreHealthStatuses.Unavailable
                ? PersonalAiOsCapabilityStates.Unavailable
                : PersonalAiOsCapabilityStates.Foundation;

        return
        [
            new(
                "memory",
                "Memory",
                "knowledge",
                StableState("memory"),
                "Trí nhớ cá nhân workspace-scoped, có lifecycle và context selection.",
                ["/api/memory"],
                false,
                true,
                false),
            new(
                "knowledge",
                "Knowledge / RAG",
                "knowledge",
                StableState("documents"),
                "Kho tài liệu local với parsing, chunking, embeddings, hybrid retrieval và citations.",
                ["/api/knowledge/documents", "/api/knowledge/hybrid-search"],
                false,
                true,
                false),
            new(
                "life-context",
                "Life Context",
                "knowledge",
                ControlledState("life-context"),
                "Context đời sống có consent, retention và encrypted local storage.",
                ["/api/life-context/status", "/api/life-context/sources"],
                true,
                true,
                false),
            new(
                "tasks",
                "Tasks",
                "reasoning",
                StableState("tasks"),
                "Task Engine persistent, có dependencies, recovery, retry và step-level permission.",
                ["/api/tasks"],
                true,
                true,
                false),
            new(
                "decision-engine",
                "Decision Engine",
                "reasoning",
                ControlledState("decision-engine"),
                "Phân tích lựa chọn, evidence, trade-off và uncertainty; không auto-action.",
                ["/api/decisions/status", "/api/decisions/analyze"],
                false,
                true,
                true),
            new(
                "tools",
                "Tool Framework",
                "execution",
                StableState("tools"),
                "Tool registry, validation, permission policy, confirmation và result handling.",
                ["/api/tools"],
                true,
                true,
                false),
            new(
                "files",
                "Workspace Files",
                "execution",
                StableState("files"),
                "Filesystem sandbox có SHA guards, permission policy và Undo cho thao tác hỗ trợ.",
                ["/api/tools"],
                true,
                true,
                false),
            new(
                "automation",
                "Automation",
                "execution",
                ControlledState("automation"),
                "Scheduler persistent; background chỉ auto-run bước local READ an toàn và không auto-confirm.",
                ["/api/automations/status", "/api/automations"],
                true,
                true,
                false),
            new(
                "software-development",
                "Software Development",
                "execution",
                ControlledState("software-development"),
                "Inspect/search/git read/build/test có kiểm soát; không arbitrary shell hay Git write.",
                ["/api/development/status"],
                true,
                true,
                true),
            new(
                "desktop",
                "Desktop / Computer Use",
                "interfaces",
                ControlledState("computer-use"),
                "Computer Use có kiểm soát; capability cụ thể phụ thuộc Windows interactive session.",
                ["/api/computer/status"],
                true,
                true,
                false),
            new(
                "browser",
                "Browser",
                "interfaces",
                ControlledState("browser-agent"),
                "HTTP/HTML Browser Agent có SSRF guards; chưa có login/form submit/browser side effects.",
                ["/api/browser/status"],
                true,
                true,
                true),
            new(
                "connectors",
                "Account Connectors",
                "interfaces",
                ControlledState("connectors"),
                "Authenticated HTTPS read connector với encrypted bearer credential và write actions tắt.",
                ["/api/connectors/status", "/api/connectors"],
                true,
                true,
                true),
            new(
                "email",
                "Email",
                "interfaces",
                FoundationState("connectors"),
                "Connector-backed read foundation. Chưa có Gmail/Outlook OAuth, mailbox model hoặc send action riêng.",
                ["/api/connectors/status"],
                true,
                true,
                true),
            new(
                "calendar",
                "Calendar",
                "interfaces",
                FoundationState("life-context"),
                "Calendar snapshot có thể đi vào Life Context; chưa có provider-specific auto-sync hoặc calendar write.",
                ["/api/life-context/status", "/api/connectors/status"],
                true,
                true,
                true),
            new(
                "android",
                "Android Companion",
                "interfaces",
                ControlledState("android-companion"),
                "Paired-device companion dùng hashed token; chat có context nhưng remote tool execution bị tắt.",
                ["/api/companion/client/core", "/api/companion/admin/devices"],
                true,
                true,
                true),
            new(
                "audit",
                "Audit",
                "governance",
                StableState("audit"),
                "Audit local, workspace-scoped, giới hạn retention và không lưu raw secret/prompt content.",
                ["/api/audit", "/api/audit/summary"],
                false,
                true,
                false),
            new(
                "undo",
                "Undo",
                "governance",
                StableState("undo"),
                "Guarded inverse operations với pre-action state và explicit confirmation.",
                ["/api/undo"],
                true,
                true,
                false),
            new(
                "agent-framework",
                "Agent Framework",
                "coordination",
                ControlledState("agents"),
                "Tám agent, workflow tuần tự có checkpoint duyệt nội dung từng handoff, giới hạn thời gian và bộ kiểm tra mẫu credential; tool execution và automatic delegation vẫn tắt.",
                ["/api/agents/status", "/api/agents", "/api/agents/orchestration/status", "/api/agents/orchestration/execute"],
                false,
                true,
                true),
            new(
                "hardening",
                "Reliability / Security",
                "governance",
                StableState("hardening"),
                "Rate/resource guards, permission audit, crash marker, backup/restore và recovery foundation.",
                ["/api/hardening/status", "/api/hardening/permissions"],
                true,
                true,
                false)
        ];
    }

    private static PersonalAiOsGovernance BuildGovernance() =>
        new(
            WorkspaceIsolation: true,
            PermissionPolicy: true,
            ExplicitConfirmationForSideEffects: true,
            Audit: true,
            Undo: true,
            Hardening: true,
            BackupRestore: true,
            AutonomousAgentLoop: false,
            ParallelToolCalls: false,
            MultiAgentFramework: true,
            AgentOrchestration: true);
}
