using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorExecutionResult(
    ComputerActionResponse Action,
    TextInteractionResult? TextInteraction,
    string Route,
    string Executor);

public interface IComputerOperatorExecutionCoordinator
{
    ComputerOperatorExecutionResult Execute(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame frame);
}

public sealed class ComputerOperatorExecutionCoordinator(
    IComputerUseService computer,
    IComputerOperatorActionExecutor actionExecutor,
    IGenericTextInteractionEngine textInteraction)
    : IComputerOperatorExecutionCoordinator
{
    public ComputerOperatorExecutionResult Execute(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame frame)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(frame);

        if (decision.Action.Equals(
                "type-text",
                StringComparison.OrdinalIgnoreCase))
        {
            var active =
                computer.GetActiveWindow()
                ?? throw new ToolExecutionInputException(
                    "Không xác định được foreground window cho Text Engine.");

            var text =
                string.IsNullOrWhiteSpace(decision.Text)
                    ? throw new ToolExecutionInputException(
                        "Thiếu text cho type-text.")
                    : decision.Text;

            var result =
                textInteraction.Execute(
                    new TextInteractionRequest(
                        active.WindowId,
                        text,
                        TextInteractionWriteModes.ReplaceAll));

            return new(
                new ComputerActionResponse(
                    ComputerUseCapabilities.TypeText,
                    result.Applied || result.Verified,
                    result.Detail),
                result,
                "text-engine",
                "text-engine");
        }

        var executed =
            actionExecutor.Execute(
                decision,
                frame);

        return new(
            executed,
            null,
            "computer",
            decision.Action.StartsWith(
                "structured-",
                StringComparison.OrdinalIgnoreCase)
                ? "uia-structured"
                : "computer-input");
    }
}
