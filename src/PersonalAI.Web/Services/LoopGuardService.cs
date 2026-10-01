using System.Collections.Concurrent;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ILoopGuardService
{
    LoopGuardStatus GetStatus(string workspaceId);
    LoopGuardDecision Check(
        string workspaceId,
        LoopGuardCheckRequest request);
    void Reset(string workspaceId, string scope, string runId);
}

public sealed class LoopGuardService(
    IAuditRecorder audit) : ILoopGuardService
{
    public const int MaximumRepeatedFingerprint = 3;
    public const int MaximumTransitionsPerRun = 50;
    public const int MaximumCostUnitsPerRun = 100;
    public const int WindowMinutes = 60;
    public const int MaximumTrackedRuns = 256;
    public const int MaximumTrackedFingerprints = 2_048;
    public const int MaximumRunIdCharacters = 160;
    public const int MaximumFingerprintCharacters = 240;

    private readonly object _gate = new();
    private readonly Dictionary<string, RunState> _runs =
        new(StringComparer.Ordinal);
    private long _blockedChecks;

    public LoopGuardStatus GetStatus(string workspaceId)
    {
        var workspace = NormalizeWorkspace(workspaceId);
        lock (_gate)
        {
            Cleanup(DateTimeOffset.UtcNow);
            return new(
                PersonalAiRelease.Version,
                workspace,
                MaximumRepeatedFingerprint,
                MaximumTransitionsPerRun,
                MaximumCostUnitsPerRun,
                WindowMinutes,
                MaximumTrackedRuns,
                MaximumTrackedFingerprints,
                DuplicateFingerprintDetectionEnabled: true,
                RunawayCostDetectionEnabled: true,
                BranchExplosionGuardEnabled: true,
                DuplicateTaskGuardEnabled: true,
                StopReasonAudited: true,
                FailClosed: true,
                LoopGuardScopes.All.Order(StringComparer.Ordinal).ToArray(),
                _runs.Values.Count(x => x.WorkspaceId == workspace),
                _runs.Values
                    .Where(x => x.WorkspaceId == workspace)
                    .Sum(x => x.Fingerprints.Count),
                Interlocked.Read(ref _blockedChecks));
        }
    }

    public LoopGuardDecision Check(
        string workspaceId,
        LoopGuardCheckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workspace = NormalizeWorkspace(workspaceId);
        var scope = NormalizeScope(request.Scope);
        var runId = NormalizeRunId(request.RunId);
        var fingerprint = NormalizeFingerprint(request.Fingerprint);
        var costUnits = request.CostUnits;

        if (costUnits is < 0 or > MaximumCostUnitsPerRun)
            throw new LoopGuardValidationException(
                $"CostUnits phải từ 0 đến {MaximumCostUnitsPerRun}.");

        var now = DateTimeOffset.UtcNow;
        LoopGuardDecision decision;

        lock (_gate)
        {
            Cleanup(now);

            var key = Key(workspace, scope, runId);
            if (!_runs.TryGetValue(key, out var state))
            {
                if (_runs.Count >= MaximumTrackedRuns)
                {
                    decision = Blocked(
                        workspace,
                        scope,
                        runId,
                        fingerprint,
                        "tracked-run-capacity",
                        "Loop guard đã đạt giới hạn số run đang theo dõi; fail-closed.",
                        0,
                        0,
                        0,
                        now,
                        now);
                    RecordBlocked(decision);
                    return decision;
                }

                state = new RunState(
                    workspace,
                    scope,
                    runId,
                    now);
                _runs[key] = state;
            }

            if (!state.Fingerprints.ContainsKey(fingerprint)
                && TotalFingerprints() >= MaximumTrackedFingerprints)
            {
                decision = Blocked(
                    workspace,
                    scope,
                    runId,
                    fingerprint,
                    "tracked-fingerprint-capacity",
                    "Loop guard đã đạt giới hạn fingerprint đang theo dõi; fail-closed.",
                    0,
                    state.Transitions,
                    state.CostUnits,
                    state.WindowStartedAt,
                    now);
                RecordBlocked(decision);
                return decision;
            }

            state.Transitions++;
            state.CostUnits += costUnits;
            state.LastSeenAt = now;

            var occurrences = state.Fingerprints.TryGetValue(
                fingerprint,
                out var current)
                ? current + 1
                : 1;
            state.Fingerprints[fingerprint] = occurrences;

            string? stopReason = null;
            string reason = "allowed";

            if (occurrences > MaximumRepeatedFingerprint)
            {
                reason = "repeated-fingerprint";
                stopReason =
                    $"Cùng fingerprint đã lặp {occurrences} lần trong cửa sổ {WindowMinutes} phút.";
            }
            else if (state.Transitions > MaximumTransitionsPerRun)
            {
                reason = "transition-budget-exceeded";
                stopReason =
                    $"Run đã vượt {MaximumTransitionsPerRun} transitions.";
            }
            else if (state.CostUnits > MaximumCostUnitsPerRun)
            {
                reason = "cost-budget-exceeded";
                stopReason =
                    $"Run đã vượt budget {MaximumCostUnitsPerRun} cost units.";
            }

            decision = stopReason is null
                ? new(
                    workspace,
                    scope,
                    runId,
                    fingerprint,
                    Allowed: true,
                    Blocked: false,
                    reason,
                    StopReason: null,
                    occurrences,
                    state.Transitions,
                    state.CostUnits,
                    state.WindowStartedAt,
                    now)
                : Blocked(
                    workspace,
                    scope,
                    runId,
                    fingerprint,
                    reason,
                    stopReason,
                    occurrences,
                    state.Transitions,
                    state.CostUnits,
                    state.WindowStartedAt,
                    now);
        }

        if (decision.Blocked)
            RecordBlocked(decision);

        return decision;
    }

    public void Reset(
        string workspaceId,
        string scope,
        string runId)
    {
        var workspace = NormalizeWorkspace(workspaceId);
        var normalizedScope = NormalizeScope(scope);
        var normalizedRun = NormalizeRunId(runId);

        lock (_gate)
        {
            _runs.Remove(Key(
                workspace,
                normalizedScope,
                normalizedRun));
        }
    }

    private void RecordBlocked(LoopGuardDecision decision)
    {
        Interlocked.Increment(ref _blockedChecks);
        audit.Record(
            AuditAgents.System,
            "loop-guard.blocked",
            $"{decision.Scope}:{decision.RunId}",
            $"{decision.Reason};{decision.StopReason}",
            AuditResults.Blocked,
            workspaceId: decision.WorkspaceId,
            level: SystemLogLevels.Security,
            source: "loop-guard",
            correlationId: decision.RunId);
    }

    private void Cleanup(DateTimeOffset now)
    {
        var cutoff = now.AddMinutes(-WindowMinutes);
        foreach (var key in _runs
            .Where(x => x.Value.LastSeenAt < cutoff)
            .Select(x => x.Key)
            .ToArray())
        {
            _runs.Remove(key);
        }
    }

    private int TotalFingerprints() =>
        _runs.Values.Sum(x => x.Fingerprints.Count);

    private static LoopGuardDecision Blocked(
        string workspace,
        string scope,
        string runId,
        string fingerprint,
        string reason,
        string stopReason,
        int occurrences,
        int transitions,
        int costUnits,
        DateTimeOffset windowStartedAt,
        DateTimeOffset now) =>
        new(
            workspace,
            scope,
            runId,
            fingerprint,
            Allowed: false,
            Blocked: true,
            reason,
            stopReason,
            occurrences,
            transitions,
            costUnits,
            windowStartedAt,
            now);

    private static string Key(
        string workspace,
        string scope,
        string runId) =>
        $"{workspace}|{scope}|{runId}";

    private static string NormalizeWorkspace(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (normalized.Length is < 1 or > 80)
            throw new LoopGuardValidationException(
                "WorkspaceId không hợp lệ.");
        return normalized;
    }

    private static string NormalizeScope(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (!LoopGuardScopes.All.Contains(normalized))
            throw new LoopGuardValidationException(
                "Loop guard scope không được hỗ trợ.");
        return normalized;
    }

    private static string NormalizeRunId(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > MaximumRunIdCharacters)
            throw new LoopGuardValidationException(
                $"RunId phải có từ 1 đến {MaximumRunIdCharacters} ký tự.");
        return normalized;
    }

    private static string NormalizeFingerprint(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > MaximumFingerprintCharacters)
            throw new LoopGuardValidationException(
                $"Fingerprint phải có từ 1 đến {MaximumFingerprintCharacters} ký tự.");
        return normalized;
    }

    private sealed class RunState(
        string workspaceId,
        string scope,
        string runId,
        DateTimeOffset windowStartedAt)
    {
        public string WorkspaceId { get; } = workspaceId;
        public string Scope { get; } = scope;
        public string RunId { get; } = runId;
        public DateTimeOffset WindowStartedAt { get; } = windowStartedAt;
        public DateTimeOffset LastSeenAt { get; set; } = windowStartedAt;
        public int Transitions { get; set; }
        public int CostUnits { get; set; }
        public Dictionary<string, int> Fingerprints { get; } =
            new(StringComparer.Ordinal);
    }
}
