namespace PersonalAI.Web.Models;

public sealed record SystemResourceLimitCounters(
    long AcceptedApiRequests,
    long RejectedPayloadRequests,
    long RejectedRateRequests,
    long RejectedConcurrencyRequests,
    long RejectedRateWindowCapacityRequests,
    int ActiveApiRequests,
    int PeakActiveApiRequests,
    int TrackedRateWindows);

public sealed record SystemResourceLimitStatus(
    string Version,
    string WorkspaceId,
    int MaximumApiRequestsPerMinute,
    int MaximumConcurrentApiRequests,
    long MaximumApiRequestBytes,
    int MaximumTrackedRateWindows,
    int MaximumBackupCount,
    long MaximumBackupSourceBytes,
    bool RequestRateLimitEnforced,
    bool ConcurrentRequestLimitEnforced,
    bool RequestBodyLimitEnforced,
    bool ChunkedRequestBodyLimitApplied,
    bool RateWindowMemoryLimitEnforced,
    bool RetryAfterProvidedOnThrottle,
    bool FailClosed,
    SystemResourceLimitCounters Counters);
