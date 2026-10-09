using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class ComputerOperatorProviderPlanningStatuses
{
    public const string Allowed = "allowed";
    public const string Unavailable = "unavailable";
    public const string Cooldown = "cooldown";
}

public sealed record ComputerOperatorProviderPlanningRoute(
    string Status,
    DesktopOperatorDecision? Decision);

public interface IComputerOperatorProviderPlanningRouter
{
    ComputerOperatorProviderPlanningRoute Evaluate(
        bool providerReady,
        bool providerBackoffActive,
        DateTimeOffset plannerBackoffUntil);
}

public sealed class ComputerOperatorProviderPlanningRouter(
    IComputerOperatorProviderResiliencePolicy providerResilience)
    : IComputerOperatorProviderPlanningRouter
{
    public ComputerOperatorProviderPlanningRoute Evaluate(
        bool providerReady,
        bool providerBackoffActive,
        DateTimeOffset plannerBackoffUntil)
    {
        if (!providerReady)
        {
            return new(
                ComputerOperatorProviderPlanningStatuses.Unavailable,
                providerResilience.CreateProviderUnavailableBlockedDecision());
        }

        if (providerBackoffActive)
        {
            return new(
                ComputerOperatorProviderPlanningStatuses.Cooldown,
                providerResilience.CreatePlannerCooldownWaitDecision(
                    plannerBackoffUntil));
        }

        return new(
            ComputerOperatorProviderPlanningStatuses.Allowed,
            null);
    }
}
