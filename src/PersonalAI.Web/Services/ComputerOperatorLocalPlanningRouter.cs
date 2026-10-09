using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorLocalPlan(
    DesktopOperatorDecision Decision,
    bool StructuredRoute,
    string Route);

public interface IComputerOperatorLocalPlanningRouter
{
    bool TryPlan(
        string goal,
        ComputerOperatorDesktopState desktopState,
        string history,
        out ComputerOperatorLocalPlan plan);
}

public sealed class ComputerOperatorLocalPlanningRouter(
    IDesktopLocalActionPlanner localActionPlanner)
    : IComputerOperatorLocalPlanningRouter
{
    public bool TryPlan(
        string goal,
        ComputerOperatorDesktopState desktopState,
        string history,
        out ComputerOperatorLocalPlan plan)
    {
        if (!localActionPlanner.TryPlan(
                goal,
                desktopState,
                history,
                out var decision))
        {
            plan = null!;
            return false;
        }

        var structuredRoute =
            desktopState.StructuredScene is not null &&
            !string.IsNullOrWhiteSpace(
                decision.TargetElementId);

        plan = new(
            decision,
            structuredRoute,
            structuredRoute
                ? "structured-first"
                : "local-planner");

        return true;
    }
}
