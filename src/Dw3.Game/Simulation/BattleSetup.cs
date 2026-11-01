namespace Dw3Production.Simulation;

/// <summary>The next battle (game_state.h BattleSetup).</summary>
public sealed class BattleSetup
{
    public bool RandomBattles { get; set; }

    public int Stage { get; set; }

    public int Battle { get; set; }

    public int Music { get; set; }

    public BattleEnemy?[] Enemies { get; set; } = new BattleEnemy?[3];

    public PartnerSeed?[] Partners { get; set; } = new PartnerSeed?[3];

    public int AmbushChance { get; set; }

    public int EncounterKind { get; set; }

    public int[] Blocks { get; set; } = new int[12]; // BattleConst.BlockRunAway, ...

    public bool HasPrize { get; set; }

    public int Prize { get; set; }
}
