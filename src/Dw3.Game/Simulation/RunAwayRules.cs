namespace Dw3Production.Simulation;

/// <summary>
/// Decides whether a side escapes the battle. Like the critical rolls, a run succeeds when the
/// low seven bits of the raw random value (0..127) fall under the computed chance.
/// </summary>
public static class RunAwayRules
{
    /// <summary>Returns whether a player's attempt to leave a battle succeeds.</summary>
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
