namespace Dw3Production.Simulation;

/// <summary>
/// Decides whether a side escapes the battle. Like the critical rolls, a run succeeds when the
/// low seven bits of the raw random value (0..127) fall under the computed chance.
/// </summary>
public static class RunAwayRules
{
    /// <summary>Returns whether a player's attempt to leave a battle succeeds.</summary>
    /// <remarks>The chance out of 128 starts at (attempts + 1) × 8, so each failure makes the next try easier. Paralysis halves it, then the level gap and a tenth of the speed gap are added when they favour the runner, plus the equipment bonus.</remarks>
    /// <param name="runAttempts">Attempts made so far in this battle, including this one.</param>
    /// <param name="atk">The runner's resolved stats.</param>
    /// <param name="def">The active enemy's resolved stats.</param>
    /// <param name="blockRunAway">Whether the encounter forbids running away, which always fails.</param>
    /// <param name="atkFlags">The runner's status bits. Asleep or <see cref="FighterFlags.NoSwitch"/> always fail.</param>
    /// <param name="rngValue">Raw random value. Only its low seven bits are used.</param>
    /// <returns><see langword="true"/> when the party escapes.</returns>
    public static bool TestRunAway(int runAttempts, BattleStats atk, BattleStats def, bool blockRunAway, int atkFlags, int rngValue)
    {
        if (blockRunAway || (atkFlags & (FighterFlags.Asleep | FighterFlags.NoSwitch)) != 0)
        {
            return false;
        }

        int chance = (runAttempts + 1) * 8;
        if ((atkFlags & FighterFlags.Paralyzed) != 0)
        {
            chance >>= 1;
        }

        if (atk.Level > def.Level)
        {
            chance += atk.Level - def.Level;
        }

        if (atk.Stats[BattleStat.Speed] > def.Stats[BattleStat.Speed])
        {
            chance += (atk.Stats[BattleStat.Speed] - def.Stats[BattleStat.Speed]) / 10;
        }

        chance += atk.RunAwayBonus;
        return (rngValue & 0x7f) < chance;
    }

    /// <summary>Returns whether an enemy attempt to leave a battle succeeds.</summary>
    /// <remarks>The chance out of 128 is 64, or 32 when the player's equipment guards against escapes, halved by paralysis, plus a tenth of the speed gap (which may be negative).</remarks>
    /// <param name="atk">The enemy's resolved stats.</param>
    /// <param name="def">The active partner's resolved stats, whose run away guard applies.</param>
    /// <param name="atkFlags">The enemy's status bits. An asleep enemy never escapes.</param>
    /// <param name="rngValue">Raw random value. Only its low seven bits are used.</param>
    /// <returns><see langword="true"/> when the enemy escapes.</returns>
    public static bool TestEnemyRunAway(BattleStats atk, BattleStats def, int atkFlags, int rngValue)
    {
        if ((atkFlags & FighterFlags.Asleep) != 0)
        {
            return false;
        }

        int chance = def.RunAwayGuard != 0 ? 0x20 : 0x40;
        if ((atkFlags & FighterFlags.Paralyzed) != 0)
        {
            chance >>= 1;
        }

        chance += (atk.Stats[BattleStat.Speed] - def.Stats[BattleStat.Speed]) / 10;
        return (rngValue & 0x7f) < chance;
    }
}
