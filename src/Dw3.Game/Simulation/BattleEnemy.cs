namespace Dw3Production.Simulation;

/// <summary>An enemy of a battle (game_state.h BattleEnemy).</summary>
public sealed class BattleEnemy
{
    public int Fighter { get; set; } // 0 for none

    public int Level { get; set; }

    public int Hp { get; set; }

    public int Mp { get; set; }

    public int Strength { get; set; } // in 16ths: scales stats and techniques' power

    public BattleStats BaseStats { get; set; } = new(); // raw battle-table entry (stats/resist/family)

    public int[] Techs { get; set; } = new int[3]; // battle-table techs[0..2]

    public BattleTableAction[] Actions { get; set; } = []; // the four actions (0..2 conditional, 3 fallback)

    public BattleTableAction Counter { get; set; } // its counter-attack (condition 0 = none)
}
