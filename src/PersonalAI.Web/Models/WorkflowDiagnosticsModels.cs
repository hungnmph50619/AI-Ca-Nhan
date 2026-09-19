namespace PersonalAI.Web.Models;

// Ghi chú: tên thuộc tính là hợp đồng API, còn mọi nội dung hiển thị cho người dùng dùng tiếng Việt.
public sealed record WorkflowDiagnosis(
    Guid WorkflowId,
    string WorkspaceId,
    string Status,
    string StatusLabel,
    string Summary,
    string? StopReason,
    string? StopReasonLabel,
    int RequestedSteps,
    int CompletedSteps,
    int? FailedStep,
    IReadOnlyList<string> SuggestedChecks,
    IReadOnlyList<string> Limitations,
    bool ContainsGoals,
    bool ContainsOutputs,
    bool ContainsTokens,
    bool GrantsExecutionRights,
    DateTimeOffset LastUpdatedAt);

public sealed record WorkflowDiagnosticsStatusResponse(
    string Version,
    bool ReadOnly,
    bool WorkspaceIsolated,
    bool MemoryOnly,
    bool ContainsGoals,
    bool ContainsOutputs,
    bool ContainsTokens,
    bool GrantsExecutionRights,
    bool SelfTestPassed,
    string NextStage);
