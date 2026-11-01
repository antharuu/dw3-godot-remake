namespace Dw3Production.Simulation;

/// <summary>Which battle menu is open: the six-command root, or one of the four submenus.</summary>
public enum BattleMenuKind
{
    Root,
    Technique,
    Digivolve,
    Item,
    Switch,
    /// <summary>First page when switching in: choose the incoming partner's Digimon.</summary>
    SwitchIn,
    /// <summary>Second page when switching in: choose to swap partners or use the pair technique.</summary>
    PairSwitch,
}
