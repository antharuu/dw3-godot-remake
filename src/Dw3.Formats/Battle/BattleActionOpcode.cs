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

    /// <summary>Suspends the script for a frame count.</summary>
    Wait = 11,
}
