namespace Dw3Production.Simulation;

/// <summary>
/// One of the party's three partners as handed to a battle. It stands in for the saved party,
/// which the core does not model yet: a null entry is an empty party slot, and
/// <see cref="Stats"/> is injected resolved (the original game computes it from partner totals
/// and equipment, which are out of scope).
/// </summary>
public sealed class PartnerSeed
{
    /// <summary>Gets or sets the partner's Digimon id, from its entry in the Digimon table. For slot 0 the battle may override it with the Digimon the partner currently is.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the HP the partner enters the battle with.</summary>
    public int Hp { get; set; }

    /// <summary>Gets or sets the maximum HP.</summary>
    public int MaxHp { get; set; }

    /// <summary>Gets or sets the MP the partner enters the battle with.</summary>
    public int Mp { get; set; }

    /// <summary>Gets or sets the maximum MP.</summary>
    public int MaxMp { get; set; }

    /// <summary>Gets or sets the resolved battle stats. The battle fighter shares this object instead of copying it.</summary>
    public BattleStats Stats { get; set; } = new();
}
