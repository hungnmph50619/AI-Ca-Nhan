using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IMobileCodeCommandService
{
    MobileCodeCommandStatus GetStatus();
    IReadOnlyList<MobileCodeCommand> GetAll();
    MobileCodeCommand? Get(Guid id);
    MobileCodeCommand Submit(
        CompanionDevice companionDevice,
        SubmitMobileCodeCommandRequest request);
    Task<MobileCodeCommand> ExecuteAsync(
        Guid commandId,
        ExecuteMobileCodeCommandRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MobileCodeCommandService(
    IDeviceHubService hub,
    IDeviceCapabilityService capabilities,
    IDevelopmentRunService runs,
    IAutonomousDevelopmentService autonomous,
    IDevelopmentGitHubService github,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IMobileCodeCommandService
{
    public const int MaximumGoalCharacters = 1_500;
    public const int MaximumRepositoryHintCharacters = 240;

    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public MobileCodeCommandStatus GetStatus() =>
        new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            CompanionRequestEnabled: true,
            DirectRemoteShellEnabled: false,
            DirectMainPushAllowed: false,
            DesktopApprovalRequired: true,
            GitHubCredentialAcceptedFromPhone: false,
            PullRequestRequired: true,
            TestGateRequired: true,
            MaximumGoalCharacters);

    public IReadOnlyList<MobileCodeCommand> GetAll()
    {
        lock (_gate)
            return Load()
                .OrderByDescending(x => x.UpdatedAt)
                .ToArray();
    }

    public MobileCodeCommand? Get(Guid id)
    {
        lock (_gate)
            return Load().FirstOrDefault(x => x.Id == id);
    }

    public MobileCodeCommand Submit(
        CompanionDevice companionDevice,
        SubmitMobileCodeCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(companionDevice);
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmRequest)
            throw new MobileCodeCommandValidationException(
                "Cần ConfirmRequest=true trước khi gửi yêu cầu sửa code từ điện thoại.");

        if (companionDevice.WorkspaceId != workspace.CurrentWorkspaceId)
            throw new MobileCodeCommandValidationException(
                "Companion device không thuộc workspace hiện tại.");

        var goal = NormalizeGoal(request.Goal);
        var repositoryHint = NormalizeOptional(
            request.RepositoryHint,
            MaximumRepositoryHintCharacters);

        lock (_gate)
        {
            var all = Load();
            var now = DateTimeOffset.UtcNow;
            var command = new MobileCodeCommand(
                Guid.NewGuid(),
                workspace.CurrentWorkspaceId,
                companionDevice.Id,
                companionDevice.Name,
                goal,
                repositoryHint,
                MobileCodeCommandStatuses.PendingDesktopApproval,
                TargetDeviceId: null,
                RepositoryPath: null,
                BaseBranch: null,
                GitHubRepository: null,
                ExperimentBranch: null,
                DevelopmentRunId: null,
                PullRequestNumber: null,
                PullRequestUrl: null,
                StopReason: null,
                now,
                now,
                CompletedAt: null);

            all.Add(command);
            Save(all);

            audit.Record(
                AuditAgents.Companion,
                "mobile-code-command.submit",
                $"mobile-code-command:{command.Id:D}",
                $"companion-device:{companionDevice.Id:D};desktop-approval-required:true;remote-shell:false",
                AuditResults.Prepared,
                workspaceId: command.WorkspaceId);

            return command;
        }
    }

    public async Task<MobileCodeCommand> ExecuteAsync(
        Guid commandId,
        ExecuteMobileCodeCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmDesktopApproval)
            throw new MobileCodeCommandValidationException(
                "Cần ConfirmDesktopApproval=true trên desktop trước khi chạy lệnh sửa code.");

        var command = Get(commandId)
            ?? throw new KeyNotFoundException(
                "Không tìm thấy mobile code command.");

        if (command.Status is not (
            MobileCodeCommandStatuses.PendingDesktopApproval or
            MobileCodeCommandStatuses.AwaitingUserConfirmation))
        {
            throw new MobileCodeCommandValidationException(
                $"Command đang ở trạng thái '{command.Status}', không thể execute.");
        }

        var target = hub.ReconcileCompanionDevices(workspace.CurrentWorkspaceId)
            .FirstOrDefault(x => x.Id == request.TargetDeviceId)
            ?? throw new KeyNotFoundException(
                "Không tìm thấy target PC trong Device Hub.");

        if (target.DeviceType is not (
            DeviceHubDeviceTypes.Pc or
            DeviceHubDeviceTypes.Laptop))
        {
            throw new MobileCodeCommandValidationException(
                "Mobile code command chỉ được chạy trên PC/laptop.");
        }

        if (target.ConnectionStatus != DeviceHubConnectionStatuses.Online)
            throw new MobileCodeCommandValidationException(
                "Target PC/laptop phải online.");

        RequireCapability(target.Id, "git");
        RequireCapability(target.Id, "build");

        var repositoryPath = NormalizeRepositoryPath(request.RepositoryPath);
        var baseBranch = NormalizeBaseBranch(request.BaseBranch);
        var githubRepository = NormalizeGitHubRepository(request.GitHubRepository);
        var credentialRef = NormalizeCredentialRef(request.CredentialRef);

        var current = command;
        DevelopmentRun run;

        if (command.DevelopmentRunId is null)
        {
            var experimentBranch =
                $"experiment/mobile-{command.Id.ToString("N")[..12]}";

            run = runs.Create(
                new CreateDevelopmentRunRequest(
                    command.Goal,
                    repositoryPath,
                    experimentBranch,
                    RoadmapVersion: "2.8.9",
                    ConfirmCreate: true));

            run = runs.Advance(
                run.Id,
                new AdvanceDevelopmentRunRequest(
                    DevelopmentRunStages.Analysis,
                    "mobile-command-desktop-approved",
                    $"companion:{command.CompanionDeviceId:D};target-device:{target.Id:D}"));

            current = command with
            {
                Status = MobileCodeCommandStatuses.Running,
                TargetDeviceId = target.Id,
                RepositoryPath = repositoryPath,
                BaseBranch = baseBranch,
                GitHubRepository = githubRepository,
                ExperimentBranch = experimentBranch,
                DevelopmentRunId = run.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Persist(current);
        }
        else
        {
            run = runs.Get(command.DevelopmentRunId.Value)
                ?? throw new KeyNotFoundException(
                    "Không tìm thấy DevelopmentRun của mobile command.");

            current = command with
            {
                Status = MobileCodeCommandStatuses.Running,
                TargetDeviceId = target.Id,
                RepositoryPath = repositoryPath,
                BaseBranch = baseBranch,
                GitHubRepository = githubRepository,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Persist(current);
        }

        try
        {
            var result = await autonomous.RunAsync(
                new RunAutonomousDevelopmentRequest(
                    run.Id,
                    repositoryPath,
                    baseBranch,
                    githubRepository,
                    credentialRef,
                    DotnetTargetPath: "PersonalAI.sln",
                    MaximumTransitions: 20,
                    ConfirmAutonomousRun: true,
                    ConfirmExternalAi: request.ConfirmExternalAi,
                    ConfirmGitHubSideEffects: request.ConfirmGitHubSideEffects,
                    AllowLowRiskAutomaticMerge: false),
                cancellationToken);

            var githubReport = github.GetAll()
                .Where(x => x.DevelopmentRunId == run.Id)
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefault();

            var status = result.Completed
                ? MobileCodeCommandStatuses.Completed
                : result.StopReason == AutonomousDevelopmentStopReasons.UserConfirmationRequired
                    ? MobileCodeCommandStatuses.AwaitingUserConfirmation
                    : MobileCodeCommandStatuses.Failed;

            var updated = current with
            {
                Status = status,
                PullRequestNumber = githubReport?.PullRequestNumber,
                PullRequestUrl = githubReport?.PullRequestUrl,
                StopReason = result.StopReason,
                UpdatedAt = DateTimeOffset.UtcNow,
                CompletedAt = status == MobileCodeCommandStatuses.Completed
                    ? DateTimeOffset.UtcNow
                    : null
            };

            Persist(updated);

            audit.Record(
                AuditAgents.System,
                "mobile-code-command.execute",
                $"mobile-code-command:{command.Id:D}",
                $"development-run:{run.Id:D};target-device:{target.Id:D};status:{status};stop:{result.StopReason};pr:{githubReport?.PullRequestNumber.ToString() ?? "none"}",
                status == MobileCodeCommandStatuses.Completed
                    ? AuditResults.Succeeded
                    : AuditResults.Prepared,
                workspaceId: command.WorkspaceId);

            return updated;
        }
        catch
        {
            var failed = current with
            {
                Status = MobileCodeCommandStatuses.Failed,
                StopReason = "exception",
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Persist(failed);
            throw;
        }
    }

    private void RequireCapability(Guid deviceId, string capability)
    {
        var access = capabilities.CheckAccess(
            workspace.CurrentWorkspaceId,
            deviceId,
            capability);

        if (!access.Allowed)
            throw new MobileCodeCommandValidationException(
                $"Target device chưa có capability '{capability}': {access.Reason}.");
    }

    private void Persist(MobileCodeCommand command)
    {
        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x => x.Id == command.Id);
            if (index < 0)
                throw new KeyNotFoundException(
                    "Không tìm thấy mobile code command để cập nhật.");

            all[index] = command;
            Save(all);
        }
    }

    private string NormalizeGoal(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 3 or > MaximumGoalCharacters)
            throw new MobileCodeCommandValidationException(
                $"Goal phải có từ 3 đến {MaximumGoalCharacters:N0} ký tự.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new MobileCodeCommandValidationException(
                $"RepositoryHint không được quá {maximum} ký tự.");
        return normalized;
    }

    private static string NormalizeRepositoryPath(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().Replace('\\', '/');
        if (normalized.Length is < 1 or > 300
            || Path.IsPathRooted(normalized)
            || normalized.StartsWith("../", StringComparison.Ordinal)
            || normalized.Contains("/../", StringComparison.Ordinal))
        {
            throw new MobileCodeCommandValidationException(
                "RepositoryPath không hợp lệ.");
        }
        return normalized;
    }

    private static string NormalizeBaseBranch(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > 120
            || normalized.StartsWith("experiment/", StringComparison.OrdinalIgnoreCase))
        {
            throw new MobileCodeCommandValidationException(
                "BaseBranch không hợp lệ.");
        }
        return normalized;
    }

    private static string NormalizeGitHubRepository(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        var parts = normalized.Split('/');
        if (parts.Length != 2
            || parts.Any(x => string.IsNullOrWhiteSpace(x)))
        {
            throw new MobileCodeCommandValidationException(
                "GitHubRepository phải ở dạng owner/repository.");
        }
        return normalized;
    }

    private static string NormalizeCredentialRef(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > 160)
            throw new MobileCodeCommandValidationException(
                "CredentialRef không hợp lệ.");
        return normalized;
    }

    private List<MobileCodeCommand> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<MobileCodeCommand>>(
                File.ReadAllText(path),
                JsonOptions) ?? [];
        }
        catch (JsonException exception)
        {
            throw new MobileCodeCommandValidationException(
                $"Mobile code command state bị hỏng và fail-closed: {exception.Message}");
        }
    }

    private void Save(IReadOnlyList<MobileCodeCommand> items)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(items, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private string PathForWorkspace()
    {
        var workspaceId = workspace.CurrentWorkspaceId;
        var safe = string.Concat(workspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(
            _root,
            $"mobile-code-commands-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var configured = configuration["MobileCodeCommand:Root"];
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

        return Path.Combine(
            localData,
            "PersonalAI",
            "MobileCodeCommands");
    }
}
