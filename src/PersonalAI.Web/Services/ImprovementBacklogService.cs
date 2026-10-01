using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IImprovementBacklogService
{
    ImprovementBacklogStatus GetStatus();
    IReadOnlyList<ImprovementBacklogItem> GetAll();
    ImprovementBacklogItem? Get(Guid id);
    ImprovementBacklogItem Add(CreateImprovementBacklogRequest request);
    ImprovementBacklogItem UpdateState(Guid id, UpdateImprovementBacklogStateRequest request);
    ImportCiBacklogResult ImportFailedCiRuns();
}

public sealed class ImprovementBacklogService(
    ICiMonitorService ci,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IImprovementBacklogService
{
    public const int MaximumEvidencePerItem = 100;

    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ImprovementBacklogStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.State is ImprovementBacklogStates.Open or ImprovementBacklogStates.Planned or ImprovementBacklogStates.InProgress),
            all.Count(x => x.Priority == ImprovementPriorities.Critical &&
                           x.State is ImprovementBacklogStates.Open or ImprovementBacklogStates.Planned or ImprovementBacklogStates.InProgress),
            all.Sum(x => x.Evidence.Count),
            DeduplicationEnabled: true,
            EvidenceTraceable: true,
            CiImportEnabled: true,
            ImprovementSources.Supported);
    }

    public IReadOnlyList<ImprovementBacklogItem> GetAll()
    {
        lock (_gate)
            return Load()
                .OrderByDescending(x => PriorityRank(x.Priority))
                .ThenByDescending(x => x.LastSeenAt)
                .ToArray();
    }

    public ImprovementBacklogItem? Get(Guid id)
    {
        lock (_gate)
            return Load().FirstOrDefault(x => x.Id == id);
    }

    public ImprovementBacklogItem Add(CreateImprovementBacklogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sourceType = NormalizeSource(request.SourceType);
        var sourceId = NormalizeText(request.SourceId, 1, 200, "SourceId");
        var title = NormalizeText(request.Title, 3, 240, "Title");
        var description = NormalizeText(request.Description, 3, 4000, "Description");
        var priority = NormalizePriority(request.Priority);
        var evidenceReference = NormalizeText(
            request.EvidenceReference, 1, 500, "EvidenceReference");
        var evidenceSummary = NormalizeText(
            request.EvidenceSummary, 1, 2000, "EvidenceSummary");

        var fingerprint = Fingerprint(
            sourceType,
            sourceId,
            title,
            evidenceReference);
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x =>
                x.Fingerprint.Equals(fingerprint, StringComparison.Ordinal));

            var evidence = new ImprovementEvidence(
                Guid.NewGuid(),
                sourceType,
                sourceId,
                evidenceReference,
                evidenceSummary,
                now);

            if (index >= 0)
            {
                var current = all[index];
                var mergedEvidence = current.Evidence
                    .Concat([evidence])
                    .GroupBy(
                        x => $"{x.SourceType}|{x.SourceId}|{x.Reference}|{x.Summary}",
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderBy(x => x.ObservedAt).First())
                    .OrderByDescending(x => x.ObservedAt)
                    .Take(MaximumEvidencePerItem)
                    .OrderBy(x => x.ObservedAt)
                    .ToArray();

                all[index] = current with
                {
                    Description = description,
                    Priority = HigherPriority(current.Priority, priority),
                    Occurrences = current.Occurrences + 1,
                    Evidence = mergedEvidence,
                    LastSeenAt = now,
                    UpdatedAt = now
                };
                Save(all);

                audit.Record(
                    AuditAgents.System,
                    "improvement.backlog.dedupe",
                    $"improvement:{current.Id:D}",
                    $"source:{sourceType};sourceId:{SafeInline(sourceId, 120)};occurrences:{all[index].Occurrences}",
                    AuditResults.Succeeded);

                return all[index];
            }

            var item = new ImprovementBacklogItem(
                Guid.NewGuid(),
                workspace.CurrentWorkspaceId,
                fingerprint,
                sourceType,
                title,
                description,
                priority,
                ImprovementBacklogStates.Open,
                Occurrences: 1,
                [evidence],
                now,
                now,
                now);

            all.Add(item);
            Save(all);

            audit.Record(
                AuditAgents.System,
                "improvement.backlog.create",
                $"improvement:{item.Id:D}",
                $"source:{sourceType};sourceId:{SafeInline(sourceId, 120)};priority:{priority}",
                AuditResults.Prepared);

            return item;
        }
    }

    public ImprovementBacklogItem UpdateState(
        Guid id,
        UpdateImprovementBacklogStateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = NormalizeState(request.State);
        var reason = NormalizeText(request.Reason, 1, 1000, "Reason");

        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x => x.Id == id);
            if (index < 0)
                throw new KeyNotFoundException("Không tìm thấy backlog item.");

            var current = all[index];
            all[index] = current with
            {
                State = state,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Save(all);

            audit.Record(
                AuditAgents.User,
                "improvement.backlog.state",
                $"improvement:{current.Id:D}",
                $"from:{current.State};to:{state};reason:{SafeInline(reason, 300)}",
                AuditResults.Succeeded);

            return all[index];
        }
    }

    public ImportCiBacklogResult ImportFailedCiRuns()
    {
        var failed = ci.GetAll()
            .Where(x =>
                x.State == CiMonitorStates.Completed &&
                !string.Equals(
                    x.Conclusion,
                    "success",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var ids = new List<Guid>();
        var created = 0;
        var deduplicated = 0;

        foreach (var run in failed)
        {
            var title = $"CI failure: {run.Repository} {run.Ref ?? run.Sha ?? run.ExternalId ?? run.Id.ToString("D")}";
            var sourceId = run.ExternalId ?? run.Id.ToString("D");
            var reference = $"ci-run:{run.Id:D}";
            var summary = string.IsNullOrWhiteSpace(run.FailureLogExcerpt)
                ? $"CI conclusion={run.Conclusion ?? "unknown"}; sha={run.Sha ?? "unknown"}"
                : run.FailureLogExcerpt!;

            var fingerprint = Fingerprint(
                ImprovementSources.Ci,
                sourceId,
                title,
                reference);

            var existed = false;
            lock (_gate)
            {
                existed = Load().Any(x =>
                    x.Fingerprint.Equals(fingerprint, StringComparison.Ordinal));
            }

            var item = Add(new CreateImprovementBacklogRequest(
                ImprovementSources.Ci,
                sourceId,
                title,
                $"CI run {run.Id:D} failed for repository '{run.Repository}'.",
                ImprovementPriorities.High,
                reference,
                summary));

            if (existed) deduplicated++;
            else created++;

            ids.Add(item.Id);
        }

        audit.Record(
            AuditAgents.System,
            "improvement.backlog.import-ci",
            "improvement-backlog:ci",
            $"failed:{failed.Length};created:{created};deduplicated:{deduplicated}",
            AuditResults.Succeeded);

        return new(
            failed.Length,
            created,
            deduplicated,
            ids.Distinct().ToArray());
    }

    private static string Fingerprint(
        string sourceType,
        string sourceId,
        string title,
        string evidenceReference)
    {
        var canonical = string.Join(
            "\n",
            NormalizeForFingerprint(sourceType),
            NormalizeForFingerprint(sourceId),
            NormalizeForFingerprint(title),
            NormalizeForFingerprint(evidenceReference));
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static string NormalizeForFingerprint(string value) =>
        string.Join(
            ' ',
            value.Trim()
                .ToLowerInvariant()
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeSource(string? value)
    {
        var source = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (!ImprovementSources.Supported.Contains(source, StringComparer.Ordinal))
            throw new ImprovementBacklogValidationException(
                "SourceType phải là error, feedback, ci, benchmark, security hoặc dependency.");
        return source;
    }

    private static string NormalizePriority(string? value)
    {
        var priority = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (priority is not (
            ImprovementPriorities.Low or
            ImprovementPriorities.Medium or
            ImprovementPriorities.High or
            ImprovementPriorities.Critical))
        {
            throw new ImprovementBacklogValidationException(
                "Priority phải là low, medium, high hoặc critical.");
        }
        return priority;
    }

    private static string NormalizeState(string? value)
    {
        var state = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (state is not (
            ImprovementBacklogStates.Open or
            ImprovementBacklogStates.Planned or
            ImprovementBacklogStates.InProgress or
            ImprovementBacklogStates.Resolved or
            ImprovementBacklogStates.Dismissed))
        {
            throw new ImprovementBacklogValidationException(
                "State không hợp lệ.");
        }
        return state;
    }

    private static string HigherPriority(string current, string incoming) =>
        PriorityRank(incoming) > PriorityRank(current)
            ? incoming
            : current;

    private static int PriorityRank(string priority) =>
        priority switch
        {
            ImprovementPriorities.Critical => 4,
            ImprovementPriorities.High => 3,
            ImprovementPriorities.Medium => 2,
            ImprovementPriorities.Low => 1,
            _ => 0
        };

    private static string NormalizeText(
        string? value,
        int minimum,
        int maximum,
        string name)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length < minimum || text.Length > maximum)
            throw new ImprovementBacklogValidationException(
                $"{name} phải có từ {minimum} đến {maximum} ký tự.");
        return text;
    }

    private List<ImprovementBacklogItem> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<ImprovementBacklogItem>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<ImprovementBacklogItem> items)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(items, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_root, $"improvement-backlog-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["Development:ImprovementRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "Improvement");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string SafeInline(string value, int maximum)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }
}
