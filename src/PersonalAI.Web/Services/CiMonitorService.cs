using System.Text.Json;
using System.Text.RegularExpressions;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ICiMonitorService
{
    CiMonitorStatus GetStatus();
    IReadOnlyList<CiRunRecord> GetAll();
    CiRunRecord? Get(Guid id);
    IReadOnlyList<CiRunRecord> RefreshFromEvents();
    CiRunRecord RecordFailureLog(RecordCiFailureLogRequest request);
    CiRepairDecision RequestRepair(RequestCiRepairRequest request);
    CiRunRecord MarkTimedOut(MarkCiRunTimedOutRequest request);
    CiRunRecord Cancel(CancelCiRunRequest request);
}

public sealed class CiMonitorService(
    IDevelopmentEventBus eventBus,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : ICiMonitorService
{
    public const int MaximumRepairAttempts = 3;
    public const int MaximumFailureLogCharacters = 32_000;

    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly Regex SecretPattern = new(
        @"(?ix)
        (?:
          gh[pousr]_[A-Za-z0-9]{20,} |
          github_pat_[A-Za-z0-9_]{20,} |
          sk-[A-Za-z0-9_-]{16,} |
          AKIA[A-Z0-9]{16} |
          authorization\s*:\s*[^\r\n]+ |
          bearer\s+[A-Za-z0-9._~+\-/]+=*
        )",
        RegexOptions.Compiled);

    public CiMonitorStatus GetStatus()
    {
        var all = RefreshFromEvents();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.State is CiMonitorStates.Queued or CiMonitorStates.InProgress),
            all.Count(x => x.State == CiMonitorStates.Completed &&
                           !string.Equals(x.Conclusion, "success", StringComparison.OrdinalIgnoreCase)),
            all.Count(x => x.State == CiMonitorStates.Cancelled),
            all.Count(x => x.State == CiMonitorStates.TimedOut),
            MaximumRepairAttempts,
            TimeoutPersisted: true,
            CancellationPersisted: true,
            FailureLogsPersisted: true,
            FullGitHubLogDownloadEnabled: false);
    }

    public IReadOnlyList<CiRunRecord> GetAll()
    {
        RefreshFromEvents();
        lock (_gate)
            return Load().OrderByDescending(x => x.UpdatedAt).ToArray();
    }

    public CiRunRecord? Get(Guid id)
    {
        RefreshFromEvents();
        lock (_gate)
            return Load().FirstOrDefault(x => x.Id == id);
    }

    public IReadOnlyList<CiRunRecord> RefreshFromEvents()
    {
        var events = eventBus.GetRecent(500)
            .Where(x => x.EventType is GitHubWebhookEvents.WorkflowRun or GitHubWebhookEvents.CheckRun)
            .OrderBy(x => x.ReceivedAt)
            .ToArray();

        lock (_gate)
        {
            var all = Load();

            foreach (var item in events)
            {
                var index = all.FindIndex(x =>
                    SameCiIdentity(x, item));

                var mappedState = MapState(item.Status, item.Conclusion);
                var completedAt = mappedState == CiMonitorStates.Completed
                    ? item.ReceivedAt
                    : (DateTimeOffset?)null;

                if (index < 0)
                {
                    all.Add(new CiRunRecord(
                        Guid.NewGuid(),
                        workspace.CurrentWorkspaceId,
                        item.Repository,
                        item.EventType,
                        item.DeliveryId,
                        item.ExternalId,
                        item.Ref,
                        item.Sha,
                        mappedState,
                        item.Conclusion,
                        RepairAttempts: 0,
                        MaximumRepairAttempts,
                        FailureLogExcerpt: null,
                        FailureLogTruncated: false,
                        item.ReceivedAt,
                        item.ReceivedAt,
                        completedAt));
                    continue;
                }

                var existing = all[index];

                if (existing.State is CiMonitorStates.Cancelled or CiMonitorStates.TimedOut)
                    continue;

                all[index] = existing with
                {
                    Repository = item.Repository,
                    SourceEventType = item.EventType,
                    Ref = item.Ref ?? existing.Ref,
                    Sha = item.Sha ?? existing.Sha,
                    State = mappedState,
                    Conclusion = item.Conclusion ?? existing.Conclusion,
                    UpdatedAt = item.ReceivedAt > existing.UpdatedAt
                        ? item.ReceivedAt
                        : existing.UpdatedAt,
                    CompletedAt = completedAt ?? existing.CompletedAt
                };
            }

            Save(all);
            return all.OrderByDescending(x => x.UpdatedAt).ToArray();
        }
    }

    public CiRunRecord RecordFailureLog(RecordCiFailureLogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sanitized = SanitizeLog(request.LogExcerpt, out var truncated);

        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x => x.Id == request.RunId);
            if (index < 0)
                throw new KeyNotFoundException("Không tìm thấy CI run.");

            var current = all[index];
            if (current.State is CiMonitorStates.Cancelled or CiMonitorStates.TimedOut)
                throw new CiMonitorValidationException(
                    "Không ghi failure log vào CI run đã cancel/timeout.");

            all[index] = current with
            {
                FailureLogExcerpt = sanitized,
                FailureLogTruncated = truncated,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Save(all);

            audit.Record(
                AuditAgents.System,
                "development.ci.failure-log",
                $"ci-run:{current.Id:D}",
                $"characters:{sanitized.Length};truncated:{truncated};secret:redacted",
                AuditResults.Succeeded);

            return all[index];
        }
    }

    public CiRepairDecision RequestRepair(RequestCiRepairRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = NormalizeReason(request.Reason);

        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x => x.Id == request.RunId);
            if (index < 0)
                throw new KeyNotFoundException("Không tìm thấy CI run.");

            var current = all[index];

            if (current.State is CiMonitorStates.Cancelled or CiMonitorStates.TimedOut)
                throw new CiRepairLimitException(
                    "CI run đã cancel/timeout nên không được repair.");

            var failed =
                current.State == CiMonitorStates.Completed &&
                !string.Equals(current.Conclusion, "success", StringComparison.OrdinalIgnoreCase);

            if (!failed)
                throw new CiMonitorValidationException(
                    "Chỉ CI run đã completed và không success mới được repair.");

            if (current.RepairAttempts >= MaximumRepairAttempts)
                throw new CiRepairLimitException(
                    $"CI repair đã đạt giới hạn {MaximumRepairAttempts} vòng.");

            var attempt = current.RepairAttempts + 1;
            all[index] = current with
            {
                RepairAttempts = attempt,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Save(all);

            audit.Record(
                AuditAgents.System,
                "development.ci.repair-request",
                $"ci-run:{current.Id:D}",
                $"attempt:{attempt}/{MaximumRepairAttempts};reason:{SafeInline(reason, 300)}",
                AuditResults.Prepared);

            return new(
                current.Id,
                attempt,
                MaximumRepairAttempts,
                Allowed: true,
                reason,
                DateTimeOffset.UtcNow);
        }
    }

    public CiRunRecord MarkTimedOut(MarkCiRunTimedOutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = NormalizeReason(request.Reason);
        return SetTerminal(
            request.RunId,
            CiMonitorStates.TimedOut,
            "timed_out",
            reason,
            "development.ci.timeout");
    }

    public CiRunRecord Cancel(CancelCiRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = NormalizeReason(request.Reason);
        return SetTerminal(
            request.RunId,
            CiMonitorStates.Cancelled,
            "cancelled",
            reason,
            "development.ci.cancel");
    }

    private CiRunRecord SetTerminal(
        Guid runId,
        string state,
        string conclusion,
        string reason,
        string auditAction)
    {
        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x => x.Id == runId);
            if (index < 0)
                throw new KeyNotFoundException("Không tìm thấy CI run.");

            var current = all[index];
            if (current.State is CiMonitorStates.Cancelled or CiMonitorStates.TimedOut)
                return current;

            var now = DateTimeOffset.UtcNow;
            all[index] = current with
            {
                State = state,
                Conclusion = conclusion,
                UpdatedAt = now,
                CompletedAt = now
            };
            Save(all);

            audit.Record(
                AuditAgents.System,
                auditAction,
                $"ci-run:{current.Id:D}",
                $"reason:{SafeInline(reason, 300)}",
                AuditResults.Succeeded);

            return all[index];
        }
    }

    private static bool SameCiIdentity(
        CiRunRecord existing,
        DevelopmentEventEnvelope item)
    {
        if (!string.IsNullOrWhiteSpace(existing.ExternalId) &&
            !string.IsNullOrWhiteSpace(item.ExternalId))
        {
            return existing.SourceEventType.Equals(
                       item.EventType,
                       StringComparison.OrdinalIgnoreCase) &&
                   existing.ExternalId.Equals(
                       item.ExternalId,
                       StringComparison.OrdinalIgnoreCase);
        }

        return existing.DeliveryId.Equals(
            item.DeliveryId,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string MapState(string? status, string? conclusion)
    {
        var normalizedStatus = (status ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedConclusion = (conclusion ?? string.Empty).Trim().ToLowerInvariant();

        if (normalizedStatus == "queued")
            return CiMonitorStates.Queued;

        if (normalizedStatus is "in_progress" or "in-progress" or "requested" or "waiting" or "pending")
            return CiMonitorStates.InProgress;

        if (normalizedStatus == "completed" || normalizedConclusion.Length > 0)
            return CiMonitorStates.Completed;

        return CiMonitorStates.InProgress;
    }

    private static string SanitizeLog(string? value, out bool truncated)
    {
        var log = value ?? string.Empty;
        if (log.Length == 0)
            throw new CiMonitorValidationException(
                "Failure log excerpt không được rỗng.");

        var redacted = SecretPattern.Replace(log, "[REDACTED]");
        truncated = redacted.Length > MaximumFailureLogCharacters;
        if (truncated)
            redacted = redacted[..MaximumFailureLogCharacters];

        return redacted;
    }

    private static string NormalizeReason(string? value)
    {
        var reason = (value ?? string.Empty).Trim();
        if (reason.Length is < 1 or > 1000)
            throw new CiMonitorValidationException(
                "Reason phải có từ 1 đến 1000 ký tự.");
        return reason;
    }

    private List<CiRunRecord> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<CiRunRecord>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<CiRunRecord> runs)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(runs, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_root, $"ci-runs-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["Development:CiRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "CI");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string SafeInline(string value, int maximum)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Length <= maximum
            ? normalized
            : normalized[..maximum];
    }
}
