namespace Dw3Production.Simulation;

/// <summary>The six commands of the player's battle menu, in the order the menu lists them,
/// so the cursor index of the original menu converts directly to a value of this enum.</summary>
public enum BattleCommand
{
    /// <summary>Basic attack with the active partner, costs no MP.</summary>
    Attack,

    /// <summary>Opens the paginated technique list of the active partner.</summary>
    Technique,

    /// <summary>Opens the digivolution choice for the active partner.</summary>
    Digivolve,

    /// <summary>Replaces the active partner by one of the two reserve partners.</summary>
    Switch,

    /// <summary>Opens the paginated list of battle items.</summary>
    Item,

    /// <summary>Tries to run away. Refused when the encounter blocks escape.</summary>
    Flee,
}
