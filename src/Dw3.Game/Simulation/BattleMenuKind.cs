namespace Dw3Production.Simulation;

/// <summary>Which battle menu is open: the six-command root, or one of the four submenus.</summary>
public enum BattleMenuKind
{
    /// <summary>The six-command menu shown at the start of each player turn.</summary>
    Root,

    /// <summary>Techniques of the active partner, six per page, with L1/R1 page turns.</summary>
    Technique,

    /// <summary>Forms the active partner can digivolve into.</summary>
    Digivolve,

    /// <summary>Battle items in the bag, seven per page, with L1/R1 page turns.</summary>
    Item,

    /// <summary>The reserve partners that can replace the active one. Knocked-out partners are greyed.</summary>
    Switch,

    /// <summary>First page when switching in: choose the incoming partner's Digimon.</summary>
    SwitchIn,

    /// <summary>Second page when switching in: choose to swap partners or use the pair technique.</summary>
    PairSwitch,
}
