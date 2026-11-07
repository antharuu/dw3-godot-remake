namespace Dw3Production.Simulation;

/// <summary>Resolves the original physical and magic hit chances from already-resolved fighter stats.</summary>
public static class BattleHitRules
{
    /// <summary>Tests a physical technique's accuracy against the low seven bits of a random value.</summary>
    /// <param name="tech">The technique whose accuracy is tested.</param>
    /// <param name="attacker">The resolved attacking stats.</param>
    /// <param name="target">The resolved defending stats.</param>
    /// <param name="enemyAttacker">Whether the enemy is attacking, which gives it a one-quarter minimum chance.</param>
    /// <param name="rngValue">The raw battle random value.</param>
    /// <returns><see langword="true"/> when the hit lands.</returns>
    public static bool RollPhysical(TechData tech, BattleStats attacker, BattleStats target, bool enemyAttacker, int rngValue) =>
        (rngValue & 0x7f) < PhysicalChance(tech, attacker, target, enemyAttacker);

    /// <summary>Tests a magic technique's accuracy against the low seven bits of a random value.</summary>
    /// <param name="tech">The technique whose accuracy is tested.</param>
    /// <param name="attacker">The resolved attacking stats.</param>
    /// <param name="target">The resolved defending stats.</param>
    /// <param name="rngValue">The raw battle random value.</param>
    /// <returns><see langword="true"/> when the hit lands.</returns>
    public static bool RollMagic(TechData tech, BattleStats attacker, BattleStats target, int rngValue) =>
        (rngValue & 0x7f) < MagicChance(tech, attacker, target);

    /// <summary>Calculates physical accuracy with the disc's player and enemy asymmetry.</summary>
    /// <param name="tech">The technique whose base accuracy is adjusted.</param>
    /// <param name="attacker">The attacker's resolved stats.</param>
    /// <param name="target">The defender's resolved stats.</param>
    /// <param name="enemyAttacker">Whether the attacker is an enemy, which grants the minimum chance.</param>
    /// <returns>The unbounded chance compared with the low seven random bits.</returns>
    private static int PhysicalChance(TechData tech, BattleStats attacker, BattleStats target, bool enemyAttacker)
    {
        int level = attacker.Level - target.Level;
        int diff = attacker.Stats[BattleStat.Speed] - target.Stats[BattleStat.Speed];
        if (enemyAttacker)
        {
            int modifier = ((diff - target.Evasion) / 8) + level;
            int chance = tech.Accuracy + ((tech.Accuracy * modifier) / 128);
            return Math.Max(chance, 0x20);
        }

        int criticalPenalty = tech.Effect < BattleConst.TechEffectFirst ? attacker.CriticalBonus : 0;
        int playerModifier = ((diff + attacker.Accuracy) / 8) + level - criticalPenalty;
        return tech.Accuracy + ((tech.Accuracy * playerModifier) / 128);
    }

    /// <summary>Calculates magic accuracy, which uses wisdom and ignores physical accessory bonuses.</summary>
    /// <param name="tech">The technique whose base accuracy is adjusted.</param>
    /// <param name="attacker">The attacker's resolved stats.</param>
    /// <param name="target">The defender's resolved stats.</param>
    /// <returns>The unbounded chance compared with the low seven random bits.</returns>
    private static int MagicChance(TechData tech, BattleStats attacker, BattleStats target)
    {
        int diff = attacker.Stats[BattleStat.Wisdom] - target.Stats[BattleStat.Wisdom];
        int level = attacker.Level - target.Level;
        int modifier = (diff / 8) + level;
        return tech.Accuracy + ((tech.Accuracy * modifier) / 128);
    }
}
