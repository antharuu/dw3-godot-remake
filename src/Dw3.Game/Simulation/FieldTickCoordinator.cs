namespace Dw3Production.Simulation;

/// <summary>Coordinates mutually exclusive field modes in their fixed-tick order.</summary>
public sealed class FieldTickCoordinator
{
    private readonly IFieldTickHost _host;

    /// <summary>Initializes the field coordinator.</summary>
    /// <param name="host">Mode state and operations owned by the runtime composition root.</param>
    public FieldTickCoordinator(IFieldTickHost host)
    {
        _host = host;
    }

    /// <summary>Advances one fixed field or battle tick.</summary>
    /// <param name="onMovementRejection">Receives rejected movement details.</param>
    /// <exception cref="InvalidOperationException">The interactive session has no active field.</exception>
    public void Tick(Action<MovementResult> onMovementRejection)
    {
        if (_host.IsInTitle)
        {
            return;
        }

        if (!_host.HasField)
        {
            throw new InvalidOperationException("simulation field unavailable");
        }

        if (_host.HasActiveBattle)
        {
            _host.TickBattleMode();
            return;
        }

        TickFieldMode(onMovementRejection);
        _host.AdvanceFieldPresentation();
    }

    /// <summary>Advances the active modal field mode, then the warp machine when locomotion owns the tick.</summary>
    /// <param name="onMovementRejection">Receives rejected movement details.</param>
    private void TickFieldMode(Action<MovementResult> onMovementRejection)
    {
        if (_host.HasActiveDialogue)
        {
            _host.TickDialogueMode();
        }
        else if (_host.HasActiveTraversal)
        {
            _host.TickTraversalMode();
        }
        else
        {
            _host.TickLocomotionMode(onMovementRejection);
            _host.TickWarp();
        }
    }
}
