namespace Dw3Production.Simulation;

/// <summary>
/// Phases of the field warp state machine. Transitions run in strict order:
/// Idle → Offer → LockInput → ValidateTarget → Fade → Commit → Release → Rearm → Idle.
/// </summary>
public enum WarpState
{
    /// <summary>No warp is pending; the player is on the field.</summary>
    Idle,

    /// <summary>A warp trigger has been entered; the prompt is shown.</summary>
    Offer,

    /// <summary>The player confirmed; locomotion input is frozen while the target loads.</summary>
    LockInput,

    /// <summary>The target zone is being validated against the cache.</summary>
    ValidateTarget,

    /// <summary>The cross-fade is running; presentation drives the visual.</summary>
    Fade,

    /// <summary>The new zone is committed: position, facing and scene are applied.</summary>
    Commit,

    /// <summary>The commit is done; pending state is cleared before re-arming.</summary>
    Release,

    /// <summary>Waiting for the confirm button to be released before returning to Idle.</summary>
    Rearm,
}
