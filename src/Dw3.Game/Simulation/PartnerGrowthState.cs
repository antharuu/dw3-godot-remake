namespace Dw3Production.Simulation;

/// <summary>Stores the persistent battle-growth values for one partner.</summary>
public sealed class PartnerGrowthState
{
    /// <summary>Gets or sets the current partner level.</summary>
    public int Level { get; set; } = 1;

    /// <summary>Gets or sets the cumulative partner experience.</summary>
    public int Exp { get; set; }

    /// <summary>Gets or sets the available technique points.</summary>
    public int Tp { get; set; }

    /// <summary>Gets or sets the maximum HP.</summary>
    public int MaxHp { get; set; }

    /// <summary>Gets or sets the maximum MP.</summary>
    public int MaxMp { get; set; }

    /// <summary>Gets or sets the six battle stats in their fixed battle order.</summary>
    public int[] BattleStats { get; set; } = new int[6];

    /// <summary>Gets or sets the seven elemental resistances.</summary>
    public int[] Resistances { get; set; } = new int[7];

    /// <summary>Copies the state so a later change can be measured against it.</summary>
    /// <returns>An independent copy, arrays included.</returns>
    public PartnerGrowthState Copy() => new()
    {
        Level = Level,
        Exp = Exp,
        Tp = Tp,
        MaxHp = MaxHp,
        MaxMp = MaxMp,
        BattleStats = [.. BattleStats],
        Resistances = [.. Resistances],
    };
}
