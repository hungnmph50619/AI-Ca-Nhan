using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorOcrPlan(
    DesktopOperatorDecision Decision,
    string Route);

public interface IComputerOperatorOcrPlanningRouter
{
    bool TryPlan(
        string goal,
        ComputerOperatorDesktopState desktopState,
        out ComputerOperatorOcrPlan plan);
}

public sealed class ComputerOperatorOcrPlanningRouter(
    IDesktopOcrActionPlanner ocrActionPlanner)
    : IComputerOperatorOcrPlanningRouter
{
    public bool TryPlan(
        string goal,
        ComputerOperatorDesktopState desktopState,
        out ComputerOperatorOcrPlan plan)
    {
        if (!ocrActionPlanner.TryPlan(
                goal,
                desktopState,
                out var decision))
        {
            plan = null!;
            return false;
        }

        plan = new(
            decision,
            "ocr-local");

        return true;
    }
}
