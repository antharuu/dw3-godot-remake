namespace Dw3.Formats.Battle;

/// <summary>Opcodes used by the bounded first action-script batch.</summary>
public enum BattleActionOpcode
{
    /// <summary>Terminates the script.</summary>
    End = 0,

    /// <summary>Changes a fighter model motion.</summary>
    Model = 2,

    /// <summary>Creates a two-dimensional sprite effect.</summary>
    SpriteEffect = 3,

    /// <summary>Sets up an animation state for one actor slot. Provisional first-batch identifier (raw 4).</summary>
    Animate = 4,

    /// <summary>Applies the accumulated animation setup. Provisional first-batch identifier (raw 5).</summary>
    Apply = 5,

    /// <summary>Configures a secondary action parameter. Provisional first-batch identifier (raw 6).</summary>
    Configure = 6,

    /// <summary>Suspends the script for a frame count.</summary>
    Wait = 11,

    /// <summary>Triggers the hit or impact phase with two parameters. Provisional first-batch identifier (raw 15).</summary>
    HitTrigger = 15,
}
