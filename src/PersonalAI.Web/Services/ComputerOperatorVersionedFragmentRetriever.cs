namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorVersionedFragmentCandidate(
    ComputerOperatorProcedureFragment Fragment,
    string CompatibilityKind,
    double CompatibilityMultiplier,
    bool ExactStartState,
    double EffectiveConfidence,
    string SourceStateFingerprint);

public interface IComputerOperatorVersionedFragmentRetriever
{
    IReadOnlyList<ComputerOperatorVersionedFragmentCandidate> Retrieve(
        string currentStateFingerprint,
        ComputerOperatorProcedureContextDescriptor currentContext,
        int maximumDepth = 6,
        int maximumCandidates = 8);
}

/// <summary>
/// Tìm fragment theo hai tầng:
/// 1) exact current state;
/// 2) state cũ cùng context family nhưng version khác.
/// Same-family fragment chỉ là reference để planner revalidate; không đủ điều kiện Fast Path.
/// </summary>
public sealed class ComputerOperatorVersionedFragmentRetriever(
    IComputerOperatorProcedureFragmentRetriever fragments,
    IComputerOperatorProcedureContextStore contexts,
    IComputerOperatorProcedureContextService compatibility)
    : IComputerOperatorVersionedFragmentRetriever
{
    public IReadOnlyList<ComputerOperatorVersionedFragmentCandidate> Retrieve(
        string currentStateFingerprint,
        ComputerOperatorProcedureContextDescriptor currentContext,
        int maximumDepth = 6,
        int maximumCandidates = 8)
    {
        var limit =
            Math.Clamp(
                maximumCandidates,
                1,
                20);

        var result =
            new List<ComputerOperatorVersionedFragmentCandidate>();

        AddCandidates(
            result,
            currentStateFingerprint,
            currentContext,
            exactStartState: true,
            maximumDepth,
            limit);

        var familyStates =
            contexts.FindFamily(
                currentContext.FamilyFingerprint,
                limit: 40);

        foreach (var remembered in familyStates)
        {
            if (remembered.StateFingerprint.Equals(
                    currentStateFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var assessment =
                compatibility.Assess(
                    currentContext,
                    remembered);

            if (!assessment.Compatible)
                continue;

            var found =
                fragments.Retrieve(
                    remembered.StateFingerprint,
                    maximumDepth,
                    maximumFragments: 4);

            foreach (var fragment in found)
            {
                result.Add(
                    new(
                        fragment,
                        assessment.Kind,
                        assessment.ConfidenceMultiplier,
                        ExactStartState: false,
                        EffectiveConfidence:
                            fragment.BottleneckConfidence *
                            assessment.ConfidenceMultiplier,
                        remembered.StateFingerprint));
            }
        }

        return result
            .DistinctBy(item =>
                string.Join(
                    ">",
                    item.Fragment.Edges.Select(edge =>
                        $"{edge.FromStateFingerprint}:{edge.StrategyKey}:{edge.ToStateFingerprint}")))
            .OrderByDescending(item =>
                item.ExactStartState)
            .ThenByDescending(item =>
                item.EffectiveConfidence)
            .ThenByDescending(item =>
                item.Fragment.MinimumSuccessCount)
            .ThenByDescending(item =>
                item.Fragment.Length)
            .Take(limit)
            .ToArray();
    }

    private void AddCandidates(
        ICollection<ComputerOperatorVersionedFragmentCandidate> result,
        string stateFingerprint,
        ComputerOperatorProcedureContextDescriptor currentContext,
        bool exactStartState,
        int maximumDepth,
        int limit)
    {
        var remembered =
            contexts.Get(
                stateFingerprint);

        var assessment =
            remembered is null
                ? new ComputerOperatorContextCompatibilityAssessment(
                    ComputerOperatorContextCompatibilityKinds.ExactVersion,
                    Compatible: true,
                    ExactVersion: true,
                    ConfidenceMultiplier: 1.0,
                    "Current state chưa có context row trước đó; exact state được giữ làm nguồn chính.")
                : compatibility.Assess(
                    currentContext,
                    remembered);

        if (!assessment.Compatible)
            return;

        var found =
            fragments.Retrieve(
                stateFingerprint,
                maximumDepth,
                maximumFragments: limit);

        foreach (var fragment in found)
        {
            result.Add(
                new(
                    fragment,
                    assessment.Kind,
                    assessment.ConfidenceMultiplier,
                    exactStartState,
                    fragment.BottleneckConfidence *
                        assessment.ConfidenceMultiplier,
                    stateFingerprint));
        }
    }
}
