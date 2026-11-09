namespace Dw3Production.Simulation;

/// <summary>One enemy of an encounter as set up for the battle: the encounter entry
/// (level, HP, MP, strength) combined with the fighter table entry of its fighter.</summary>
public sealed class BattleEnemy
{
    /// <summary>Gets or sets the fighter id that selects the enemy's model, stats and AI. 0 means the slot is empty.</summary>
    public int Fighter { get; set; }

    /// <summary>Gets or sets the enemy level from the encounter entry. It feeds the experience reward and the damage formulas.</summary>
    public int Level { get; set; }

    /// <summary>Gets or sets the starting HP from the encounter entry.</summary>
    public int Hp { get; set; }

    /// <summary>Gets or sets the starting MP from the encounter entry.</summary>
    public int Mp { get; set; }

    /// <summary>Gets or sets the strength multiplier in sixteenths (16 = ×1). It scales the fighter's base stats and its techniques' power.</summary>
    public int Strength { get; set; }

    /// <summary>Gets or sets the unscaled stats, resistances and family of the fighter table entry.</summary>
    public BattleStats BaseStats { get; set; } = new();

    /// <summary>Gets or sets the three technique ids of the fighter table entry. An id of 0 is an empty slot.</summary>
    public int[] Techs { get; set; } = new int[3];

    /// <summary>Gets or sets the four AI actions of the fighter: entries 0..2 are tried in order when their condition holds, entry 3 is the fallback.</summary>
    public BattleTableAction[] Actions { get; set; } = [];

    /// <summary>Gets or sets the counter-attack the enemy uses when hit. A condition of 0 means no counter.</summary>
    public BattleTableAction Counter { get; set; }

    /// <summary>Gets or sets the item held by this enemy's fighter-table entry, 0 when it holds none.</summary>
    public int Item { get; set; }

    /// <summary>Gets or sets the chance in 1024ths that this enemy yields its held item to a steal technique.</summary>
    public int ItemChance { get; set; }
}
