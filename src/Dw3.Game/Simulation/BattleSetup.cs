namespace Dw3Production.Simulation;

/// <summary>Everything needed to start the next battle, filled by the encounter roll on the field
/// (or the debug battle key) and read once when the battle state is initialised.</summary>
public sealed class BattleSetup
{
    /// <summary>Gets or sets a value indicating whether random encounters were enabled when the battle was set up.</summary>
    public bool RandomBattles { get; set; }

    /// <summary>Gets or sets the battle stage id, which selects the background drawn behind the fighters.</summary>
    public int Stage { get; set; }

    /// <summary>Gets or sets the encounter index in the encounter table, also tested by enemy AI condition 12.</summary>
    public int Battle { get; set; }

    /// <summary>Gets or sets the music id played during the battle.</summary>
    public int Music { get; set; }

    /// <summary>Gets or sets the three enemy slots. A null slot is empty.</summary>
    public BattleEnemy?[] Enemies { get; set; } = new BattleEnemy?[3];

    /// <summary>Gets or sets the three party slots. Slot 0 starts as the active partner, a null slot is empty.</summary>
    public PartnerSeed?[] Partners { get; set; } = new PartnerSeed?[3];

    /// <summary>Gets or sets the ambush chance of the encounter entry, copied as stored. The current battle rules do not apply it.</summary>
    public int AmbushChance { get; set; }

    /// <summary>Gets or sets the encounter kind of the encounter entry, tested by enemy AI condition 13.</summary>
    public int EncounterKind { get; set; }

    /// <summary>Gets or sets the twelve block flags of the encounter. A non-zero entry forbids an action, for example <see cref="BattleConst.BlockRunAway"/> forbids running away.</summary>
    public int[] Blocks { get; set; } = new int[12];

    /// <summary>Gets or sets a value indicating whether the battle carries a <see cref="Prize"/>. Kept for scripted battles, the current fight report does not read it.</summary>
    public bool HasPrize { get; set; }

    /// <summary>Gets or sets the prize item id, meaningful only when <see cref="HasPrize"/> is set.</summary>
    public int Prize { get; set; }
}
