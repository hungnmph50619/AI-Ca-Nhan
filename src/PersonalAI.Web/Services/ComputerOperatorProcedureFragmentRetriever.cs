namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorProcedureFragment(
    string StartStateFingerprint,
    string EndStateFingerprint,
    IReadOnlyList<ComputerOperatorProcedureEdge> Edges,
    double BottleneckConfidence,
    int MinimumSuccessCount,
    DateTimeOffset OldestVerification,
    DateTimeOffset NewestVerification)
{
    public int Length => Edges.Count;
}

public interface IComputerOperatorProcedureFragmentRetriever
{
    IReadOnlyList<ComputerOperatorProcedureFragment> Retrieve(
        string startStateFingerprint,
        int maximumDepth = 6,
        int maximumFragments = 8);
}

/// <summary>
/// Tìm các đoạn Procedure Graph đã được VERIFY PASS bắt đầu từ state hiện tại.
/// Retrieval chỉ cung cấp knowledge cho planner; không tự execute và không tự bật Fast Path.
/// Duyệt có giới hạn, không đi qua cùng state hai lần để tránh cycle.
/// </summary>
public sealed class ComputerOperatorProcedureFragmentRetriever(
    IComputerOperatorProcedureGraphStore graph,
    IComputerOperatorProcedureEdgeLifecycleService? lifecycle = null)
    : IComputerOperatorProcedureFragmentRetriever
{
    public const int MaximumDepthLimit = 8;
    public const int MaximumFragmentsLimit = 20;
    public const int MaximumBranchingPerNode = 4;

    public IReadOnlyList<ComputerOperatorProcedureFragment> Retrieve(
        string startStateFingerprint,
        int maximumDepth = 6,
        int maximumFragments = 8)
    {
        var depth =
            Math.Clamp(
                maximumDepth,
                1,
                MaximumDepthLimit);

        var fragmentLimit =
            Math.Clamp(
                maximumFragments,
                1,
                MaximumFragmentsLimit);

        var queue =
            new Queue<PathState>();

        queue.Enqueue(
            new(
                startStateFingerprint,
                Array.Empty<ComputerOperatorProcedureEdge>(),
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    startStateFingerprint
                }));

        var candidates =
            new List<ComputerOperatorProcedureFragment>();

        while (queue.Count > 0 &&
               candidates.Count <
                   fragmentLimit * 4)
        {
            var current =
                queue.Dequeue();

            if (current.Edges.Count >= depth)
            {
                AddCandidate(
                    candidates,
                    startStateFingerprint,
                    current);

                continue;
            }

            var outgoing =
                graph.GetOutgoing(
                        current.StateFingerprint,
                        MaximumBranchingPerNode)
                    .Where(edge =>
                        edge.SuccessCount > 0 &&
                        edge.AverageConfidence > 0 &&
                        !(lifecycle?.IsSuperseded(edge) ?? false))
                    .ToArray();

            var expanded =
                false;

            foreach (var edge in outgoing)
            {
                if (current.VisitedStates.Contains(
                        edge.ToStateFingerprint))
                {
                    continue;
                }

                expanded =
                    true;

                var edges =
                    current.Edges
                        .Append(edge)
                        .ToArray();

                var visited =
                    new HashSet<string>(
                        current.VisitedStates,
                        StringComparer.OrdinalIgnoreCase)
                    {
                        edge.ToStateFingerprint
                    };

                queue.Enqueue(
                    new(
                        edge.ToStateFingerprint,
                        edges,
                        visited));
            }

            if (!expanded &&
                current.Edges.Count > 0)
            {
                AddCandidate(
                    candidates,
                    startStateFingerprint,
                    current);
            }
            else if (current.Edges.Count > 0)
            {
                // Giữ cả fragment trung gian để task mới có thể chỉ cần một prefix.
                AddCandidate(
                    candidates,
                    startStateFingerprint,
                    current);
            }
        }

        return candidates
            .DistinctBy(FragmentIdentity)
            .OrderByDescending(item =>
                item.BottleneckConfidence)
            .ThenByDescending(item =>
                item.MinimumSuccessCount)
            .ThenByDescending(item =>
                item.Length)
            .ThenByDescending(item =>
                item.NewestVerification)
            .Take(fragmentLimit)
            .ToArray();
    }

    private void AddCandidate(
        ICollection<ComputerOperatorProcedureFragment> candidates,
        string startStateFingerprint,
        PathState state)
    {
        if (state.Edges.Count == 0)
            return;

        candidates.Add(
            new(
                startStateFingerprint,
                state.StateFingerprint,
                state.Edges,
                state.Edges.Min(edge =>
                    lifecycle?.Evaluate(edge).EffectiveConfidence ??
                    edge.AverageConfidence),
                state.Edges.Min(edge =>
                    edge.SuccessCount),
                state.Edges.Min(edge =>
                    edge.FirstVerifiedAt),
                state.Edges.Max(edge =>
                    edge.LastVerifiedAt)));
    }

    private static string FragmentIdentity(
        ComputerOperatorProcedureFragment fragment) =>
        string.Join(
            ">",
            fragment.Edges.Select(edge =>
                $"{edge.FromStateFingerprint}:{edge.StrategyKey}:{edge.ToStateFingerprint}"));

    private sealed record PathState(
        string StateFingerprint,
        IReadOnlyList<ComputerOperatorProcedureEdge> Edges,
        HashSet<string> VisitedStates);
}
