namespace PersonalAI.Web.Services;

public enum ComputerOperatorActionState
{
    Idle,
    Observe,
    Plan,
    Target,
    Execute,
    Verify,
    Success,
    Diagnose,
    Replan,
    Blocked
}

public sealed record ComputerOperatorActionStateSnapshot(
    ComputerOperatorActionState State,
    int TransitionCount,
    string Detail);

public sealed class ComputerOperatorActionStateMachine
{
    private ComputerOperatorActionState _state =
        ComputerOperatorActionState.Idle;
    private int _transitionCount;

    public ComputerOperatorActionState State => _state;

    public ComputerOperatorActionStateSnapshot MoveTo(
        ComputerOperatorActionState next,
        string detail)
    {
        if (!CanTransition(_state, next))
        {
            throw new InvalidOperationException(
                $"Chuyển trạng thái Computer Operator không hợp lệ: {_state} -> {next}.");
        }

        _state = next;
        _transitionCount++;

        return new(
            _state,
            _transitionCount,
            detail);
    }

    public ComputerOperatorActionStateSnapshot StartObservation(
        string detail) =>
        MoveTo(
            ComputerOperatorActionState.Observe,
            detail);

    public ComputerOperatorActionStateSnapshot ResetForNextStep(
        string detail)
    {
        if (_state is not
            ComputerOperatorActionState.Success and not
            ComputerOperatorActionState.Replan and not
            ComputerOperatorActionState.Blocked and not
            ComputerOperatorActionState.Observe)
        {
            throw new InvalidOperationException(
                $"Không thể bắt đầu bước mới từ trạng thái {_state}.");
        }

        _state = ComputerOperatorActionState.Observe;
        _transitionCount++;

        return new(
            _state,
            _transitionCount,
            detail);
    }

    private static bool CanTransition(
        ComputerOperatorActionState current,
        ComputerOperatorActionState next) =>
        (current, next) switch
        {
            (ComputerOperatorActionState.Idle,
                ComputerOperatorActionState.Observe) => true,

            (ComputerOperatorActionState.Observe,
                ComputerOperatorActionState.Plan) => true,

            (ComputerOperatorActionState.Plan,
                ComputerOperatorActionState.Target) => true,
            (ComputerOperatorActionState.Plan,
                ComputerOperatorActionState.Success) => true,
            (ComputerOperatorActionState.Plan,
                ComputerOperatorActionState.Blocked) => true,
            (ComputerOperatorActionState.Plan,
                ComputerOperatorActionState.Replan) => true,

            (ComputerOperatorActionState.Target,
                ComputerOperatorActionState.Execute) => true,
            (ComputerOperatorActionState.Target,
                ComputerOperatorActionState.Replan) => true,
            (ComputerOperatorActionState.Target,
                ComputerOperatorActionState.Blocked) => true,

            (ComputerOperatorActionState.Execute,
                ComputerOperatorActionState.Verify) => true,
            (ComputerOperatorActionState.Execute,
                ComputerOperatorActionState.Diagnose) => true,

            (ComputerOperatorActionState.Verify,
                ComputerOperatorActionState.Success) => true,
            (ComputerOperatorActionState.Verify,
                ComputerOperatorActionState.Diagnose) => true,

            (ComputerOperatorActionState.Diagnose,
                ComputerOperatorActionState.Replan) => true,
            (ComputerOperatorActionState.Diagnose,
                ComputerOperatorActionState.Blocked) => true,

            (ComputerOperatorActionState.Success,
                ComputerOperatorActionState.Observe) => true,
            (ComputerOperatorActionState.Replan,
                ComputerOperatorActionState.Observe) => true,
            (ComputerOperatorActionState.Blocked,
                ComputerOperatorActionState.Observe) => true,

            _ => false
        };
}
