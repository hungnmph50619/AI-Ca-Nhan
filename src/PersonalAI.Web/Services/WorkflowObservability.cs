using System.Collections.Concurrent;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

/// <summary>
/// Bounded, metadata-only, server-local workflow observations. Never stores
/// goals, model outputs, checkpoint previews, tokens, hashes or exception text.
/// </summary>
public static class WorkflowObservability
{
    public const int MaximumRecentWorkflows = 100;
    public const int RetentionMinutes = 60;

    private static readonly ConcurrentDictionary<Guid, WorkflowObservation> Recent = new();

    public static WorkflowObservabilityStatusResponse GetStatus() =>
        new(
            PersonalAiRelease.Version,
            MaximumRecentWorkflows,
            RetentionMinutes,
            MemoryOnly: true,
            WorkspaceIsolated: true,
            ContainsGoals: false,
            ContainsOutputs: false,
            ContainsReviewTokens: false,
            ContainsCredentialValues: false,
            GrantsExecutionRights: false,
            NextStage: "v2.2.7-workflow-usability");

    public static WorkflowObservation Capture(AgentWorkflowResponse response)
    {
        var now = DateTimeOffset.UtcNow;
        var observation = new WorkflowObservation(
            response.WorkflowId,
            response.WorkspaceId,
            response.Status,
            response.RequestedSteps,
            response.CompletedSteps.Count,
            response.FailedStep,
            SanitizeReason(response.StopReason),
            response.Status == AgentWorkflowStatuses.AwaitingReview,
            response.StartedAt,
            now,
            Math.Max(0, (long)(now - response.StartedAt).TotalMilliseconds));

        Recent[response.WorkflowId] = observation;
        Prune(now);
        return observation;
    }

    public static WorkflowObservation? Find(Guid workflowId, string workspaceId)
    {
        Prune(DateTimeOffset.UtcNow);
        return Recent.TryGetValue(workflowId, out var entry)
            && string.Equals(entry.WorkspaceId, workspaceId, StringComparison.Ordinal)
                ? entry : null;
    }

    private static string? SanitizeReason(string? reason) => reason switch
    {
        "timeout" or "step-failed" or "validation-blocked" or "review-declined"
            => reason,
        _ => null
    };

    private static void Prune(DateTimeOffset now)
    {
        foreach (var item in Recent)
            if (item.Value.LastUpdatedAt.AddMinutes(RetentionMinutes) <= now)
                Recent.TryRemove(item.Key, out _);

        var extra = Recent.Count - MaximumRecentWorkflows;
        if (extra <= 0) return;
        foreach (var old in Recent.Values.OrderBy(x => x.LastUpdatedAt).Take(extra))
            Recent.TryRemove(old.WorkflowId, out _);
    }

    public static bool RunSelfTest()
    {
        var id = Guid.NewGuid();
        const string secret = "SecretMustNeverBeStored123";
        var example = new AgentWorkflowResponse(
            id, "workspace-A", AgentWorkflowStatuses.Stopped,
            [], 2, 1, secret, false, false, false, false,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            secret, null);
        try
        {
            var observation = Capture(example);
            return observation.StopReason is null
                && Find(id, "workspace-A") is not null
                && Find(id, "workspace-B") is null
                && !observation.ToString().Contains(secret, StringComparison.Ordinal)
                && !GetStatus().GrantsExecutionRights;
        }
        finally
        {
            Recent.TryRemove(id, out _);
        }
    }
}
