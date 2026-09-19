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
    IWorkspaceContextAccessor workspaceContext) : IPersonalAiOsService
{
    public const string Edition = "Personal AI OS";
    public const string Stage = "v2.2.1";
    public const string NextStage = "v2.2.2-orchestration-reliability";

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
            ["audit", "undo", "hardening"])
    ];

    private static readonly string[] Boundaries =
    [
        "v2.2.1 giới hạn thời gian workflow/bước và kiểm tra mẫu bí mật trước khi chuyển kết quả; danh sách agent vẫn do người dùng xác nhận trước.",
        "Planner chỉ lập kế hoạch; Research đọc tài liệu nội bộ; Developer đề xuất thay đổi; Office soạn nháp; Operator chuẩn bị thao tác; Reviewer không phê duyệt; Security không thay permission gate hay chứng nhận an toàn.",
        "v2.2.1 chỉ chuyển đầu ra khi người dùng đồng ý và dữ liệu qua kiểm tra mẫu; bộ kiểm tra giới hạn không bảo đảm phát hiện mọi bí mật, cũng không bật automatic delegation, shared task queue, autonomous loop hay parallel calls.",
        "WRITE/DELETE/EXTERNAL/SENSITIVE/COMPUTER/BROWSER/CONNECTOR/DEVELOPMENT vẫn yêu cầu policy và confirmation tương ứng.",
        "Email vẫn là connector-backed foundation, chưa có Gmail/Outlook provider-specific OAuth.",
        "Calendar vẫn dùng Life Context snapshot + connector foundation, chưa có provider-specific auto-sync/write.",
        "Decision Engine chỉ phân tích và recommendation; người dùng vẫn là người ra quyết định cuối cùng.",
        "Automation không tự xác nhận side effect và chỉ tiến tối đa một task step mỗi scheduler tick."
    ];

    public async Task<PersonalAiOsStatusResponse> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var health = await core.GetHealthAsync(cancellationToken);
        var capabilities = BuildCapabilities(health.Modules);
        var readiness = BuildReadiness(health, capabilities);
        var workspace = workspaceContext.CurrentWorkspace;

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
            NextStage);
    }

    public async Task<PersonalAiOsManifestResponse> GetManifestAsync(
        CancellationToken cancellationToken = default)
    {
        var health = await core.GetHealthAsync(cancellationToken);
        return new PersonalAiOsManifestResponse(
            PersonalAiRelease.Version,
            Edition,
            Stage,
            Layers,
            BuildCapabilities(health.Modules),
            BuildGovernance(),
            Boundaries);
    }

    private static PersonalAiOsReadiness BuildReadiness(
        SystemHealthResponse health,
        IReadOnlyList<PersonalAiOsCapability> capabilities)
    {
        var blockers = new List<string>();

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
                "Tám agent, workflow tuần tự 2–3 bước có opt-in handoff, giới hạn thời gian và kiểm tra mẫu credential trước chuyển giao; tool execution và automatic delegation vẫn tắt.",
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
