namespace Dw3Production.Simulation;

/// <summary>
/// Phases of the SLOT_DROP action state machine. Transitions run in order:
/// DropOff → Fall → Land → (back to None via EndDrop).
/// </summary>
public enum DropSubstate
{
    /// <summary>No drop is active; the controller is idle.</summary>
    None,

    /// <summary>The drop-off animation plays at the cliff edge before the fall begins.</summary>
    DropOff,

    /// <summary>The actor is descending: climbHeight decreases by FallStep each tick.</summary>
    Fall,

    /// <summary>The landing animation plays; field control is restored on completion.</summary>
    Land,
}
