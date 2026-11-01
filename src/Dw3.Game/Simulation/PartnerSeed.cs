namespace Dw3Production.Simulation;

/// <summary>
/// One of the party's three partners. An adaptation of GAME.partners read through
/// getPartyMember/getPartnerStats, which the core does not model yet: a null entry is an
/// empty party slot, and Stats is injected resolved (the source computes it from partner
/// totals + equipment, which are out of scope).
/// </summary>
public sealed class PartnerSeed
{
    public int Id { get; set; } // DIGIMON_DATA[partner].id (overridden by the digimon arg in slot 0)

    public int Hp { get; set; }

    public int MaxHp { get; set; }

    public int Mp { get; set; }

    public int MaxMp { get; set; }

    public BattleStats Stats { get; set; } = new();
}
