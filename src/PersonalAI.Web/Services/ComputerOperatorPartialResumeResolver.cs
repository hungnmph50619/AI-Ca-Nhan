namespace PersonalAI.Web.Services;

public static class ComputerOperatorResumeKinds
{
    public const string Fresh = "fresh";
    public const string ExactCheckpointState = "exact-checkpoint-state";
    public const string KnownGraphNode = "known-graph-node";
    public const string UnknownObservedState = "unknown-observed-state";
}

public sealed record ComputerOperatorPartialResumeAssessment(
    string Kind,
    bool Resumable,
    bool ExactCheckpointMatch,
    bool KnownProcedureNode,
    int OutgoingEdges,
    int IncomingEdges,
    string Reason);

public interface IComputerOperatorPartialResumeResolver
{
    ComputerOperatorPartialResumeAssessment Assess(
        ComputerOperatorCheckpoint? checkpoint,
        string observedStateFingerprint);
}

/// <summary>
/// Quyết định cách resume chỉ sau khi đã re-observe desktop.
/// Không replay action từ checkpoint; chỉ xác định state hiện tại có khớp checkpoint
/// hoặc có thể join vào Procedure Graph đã biết hay không.
/// </summary>
public sealed class ComputerOperatorPartialResumeResolver(
    IComputerOperatorProcedureGraphStore graph)
    : IComputerOperatorPartialResumeResolver
{
    public ComputerOperatorPartialResumeAssessment Assess(
        ComputerOperatorCheckpoint? checkpoint,
        string observedStateFingerprint)
    {
        var observed =
            NormalizeFingerprint(
                observedStateFingerprint);

        if (checkpoint is null)
        {
            return new(
                ComputerOperatorResumeKinds.Fresh,
                Resumable: false,
                ExactCheckpointMatch: false,
                KnownProcedureNode: false,
                OutgoingEdges: 0,
                IncomingEdges: 0,
                "Không có checkpoint cần resume.");
        }

        var outgoing =
            graph.GetOutgoing(
                observed,
                limit: 20);

        var incoming =
            graph.GetIncoming(
                observed,
                limit: 20);

        var knownNode =
            outgoing.Count > 0 ||
            incoming.Count > 0;

        var exact =
            !string.IsNullOrWhiteSpace(
                checkpoint.LastObservedStateFingerprint) &&
            string.Equals(
                checkpoint.LastObservedStateFingerprint,
                observed,
                StringComparison.OrdinalIgnoreCase);

        if (exact)
        {
            return new(
                ComputerOperatorResumeKinds.ExactCheckpointState,
                Resumable: true,
                ExactCheckpointMatch: true,
                KnownProcedureNode: knownNode,
                OutgoingEdges: outgoing.Count,
                IncomingEdges: incoming.Count,
                "Desktop hiện tại khớp state đã lưu trong checkpoint; tiếp tục từ state này, không replay action cuối.");
        }

        if (knownNode)
        {
            return new(
                ComputerOperatorResumeKinds.KnownGraphNode,
                Resumable: true,
                ExactCheckpointMatch: false,
                KnownProcedureNode: true,
                OutgoingEdges: outgoing.Count,
                IncomingEdges: incoming.Count,
                "Desktop hiện tại khác checkpoint nhưng là node đã biết trong Procedure Graph; join vào graph tại state hiện tại thay vì quay lại đầu.");
        }

        return new(
            ComputerOperatorResumeKinds.UnknownObservedState,
            Resumable: true,
            ExactCheckpointMatch: false,
            KnownProcedureNode: false,
            OutgoingEdges: 0,
            IncomingEdges: 0,
            "Desktop hiện tại khác checkpoint và chưa có trong Procedure Graph; giữ các mốc cũ nhưng bắt buộc planner suy luận lại từ state mới.");
    }

    private static string NormalizeFingerprint(
        string value)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

        if (normalized.Length != 64 ||
            normalized.Any(character =>
                !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Observed state fingerprint phải là SHA-256 hex 64 ký tự.",
                nameof(value));
        }

        return normalized;
    }
}
