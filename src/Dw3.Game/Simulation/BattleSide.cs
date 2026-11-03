namespace Dw3Production.Simulation;

/// <summary>A side, as the turn loop and active-fighter accessors use it.</summary>
public enum BattleSide
{
    /// <summary>The player's party, controlled through the battle menu.</summary>
    Player,

    /// <summary>The encounter's enemies, controlled by their action tables.</summary>
    Enemy,
}
