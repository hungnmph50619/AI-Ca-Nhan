namespace PersonalAI.Web.Models;

public static class GitHubWebhookEvents
{
    public const string Push = "push";
    public const string PullRequest = "pull_request";
    public const string PullRequestReview = "pull_request_review";
    public const string WorkflowRun = "workflow_run";
    public const string CheckRun = "check_run";
    public const string Release = "release";

    public static readonly IReadOnlyList<string> Supported =
    [
        Push,
        PullRequest,
        PullRequestReview,
        WorkflowRun,
        CheckRun,
        Release
    ];
}

public sealed record ConfigureGitHubWebhookRequest(
    string Secret,
    bool ConfirmStoreSecret = false);

public sealed record GitHubWebhookStatus(
    string Version,
    string WorkspaceId,
    bool Configured,
    bool SignatureRequired,
    string SignatureAlgorithm,
    bool DeliveryIdRequired,
    bool IdempotencyEnabled,
    int ProcessedDeliveries,
    IReadOnlyList<string> SupportedEvents);

public sealed record DevelopmentEventEnvelope(
    Guid Id,
    string WorkspaceId,
    string Source,
    string EventType,
    string DeliveryId,
    string Repository,
    string? Action,
    string? Ref,
    string? Sha,
    int? Number,
    string? Status,
    string? Conclusion,
    string? Tag,
    DateTimeOffset ReceivedAt);

public sealed record GitHubWebhookReceiveResult(
    string DeliveryId,
    string EventType,
    bool Accepted,
    bool Duplicate,
    Guid? EventId,
    DateTimeOffset ProcessedAt);

public sealed class GitHubWebhookValidationException(string message)
    : Exception(message);

public sealed class GitHubWebhookSignatureException(string message)
    : Exception(message);
