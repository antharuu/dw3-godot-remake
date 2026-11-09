namespace Dw3Production.Simulation;

/// <summary>Names the outcome of a stock-neutral item-tech simulation request.</summary>
public enum BattleItemTechniqueUseResult
{
    /// <summary>The requested item-tech changed battle state or queued its source-defined deferred work.</summary>
    Applied,

    /// <summary>The battle has no valid active target for the request.</summary>
    InvalidTarget,

    /// <summary>The technique id and extracted effect do not identify a supported item-tech.</summary>
    UnknownEffect,

    /// <summary>The player-owned item-tech path does not accept an enemy-originated request.</summary>
    OwnerNotPlayer,

    /// <summary>The requested effect would not change battle state.</summary>
    NoBenefit,
}
