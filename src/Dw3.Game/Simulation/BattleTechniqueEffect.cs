namespace Dw3Production.Simulation;

/// <summary>Technique effect identifiers from the extracted battle catalogue.</summary>
public static class BattleTechniqueEffect
{
    /// <summary>Applies periodic poison to the opposing active fighter.</summary>
    public const int Poison = 2;

    /// <summary>Applies the turn-loss paralysis status.</summary>
    public const int Paralysis = 3;

    /// <summary>Applies the forced-action confusion status.</summary>
    public const int Confusion = 4;

    /// <summary>Applies the sleeping status.</summary>
    public const int Sleep = 5;

    /// <summary>Attempts an immediate knock-out.</summary>
    public const int KnockOut = 6;

    /// <summary>Drains a share of the landed damage as HP.</summary>
    public const int Drain = 8;

    /// <summary>Rolls additional hits.</summary>
    public const int MultiHit = 9;

    /// <summary>Restricts a technique to an enemy table entry.</summary>
    public const int EnemyOnly = 10;

    /// <summary>Raises the critical-hit chance.</summary>
    public const int Critical = 11;

    /// <summary>Steals an enemy-held item.</summary>
    public const int Steal = 12;

    /// <summary>Temporarily forbids switching.</summary>
    public const int NoSwitch = 13;

    /// <summary>Temporarily forbids digivolution.</summary>
    public const int NoDigivolve = 14;

    /// <summary>Temporarily lowers attack.</summary>
    public const int LowerAttack = 26;

    /// <summary>Temporarily lowers defense.</summary>
    public const int LowerDefense = 27;

    /// <summary>Drains a share of the target's maximum MP.</summary>
    public const int DrainMp = 29;

    /// <summary>Runs a two-hit magic action.</summary>
    public const int DoubleMagic = 31;

    /// <summary>Raises one partner status at random.</summary>
    public const int RaiseOneStatus = 32;

    /// <summary>Rolls a raise for each partner status.</summary>
    public const int RaiseEachStatus = 33;

    /// <summary>Raises every partner status.</summary>
    public const int RaiseAllStatus = 34;

    /// <summary>Runs the encounter-specific battle-ending action.</summary>
    public const int EndBattle = 35;
}
