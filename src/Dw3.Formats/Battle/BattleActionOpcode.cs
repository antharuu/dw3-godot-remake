namespace Dw3.Formats.Battle;

/// <summary>Opcodes used by the bounded first action-script batch.</summary>
public enum BattleActionOpcode
{
    /// <summary>Terminates the script.</summary>
    End = 0,

    /// <summary>Changes a fighter model motion or visibility state.</summary>
    Model = 2,

    /// <summary>Creates a two-dimensional sprite effect.</summary>
    SpriteEffect = 3,

    /// <summary>Loads a battle stage or applies a fade transition. Raw opcode 4.</summary>
    Stage = 4,

    /// <summary>Sets up a battle camera move with timing and mode parameters. Raw opcode 5.</summary>
    Camera = 5,

    /// <summary>Loads or triggers a three-dimensional effect model. Raw opcode 6.</summary>
    EffectModel = 6,

    /// <summary>Suspends the script for a frame count.</summary>
    Wait = 11,
}
