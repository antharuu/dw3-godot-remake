namespace Dw3Production.Simulation;

/// <summary>
/// Decides whether a hit is critical. A roll succeeds when the low seven bits of the raw
/// random value (0..127) fall under the computed chance, so a chance of 128 or more always hits.
/// </summary>
public static class CriticalHitRules
{
    /// <summary>Returns whether a physical technique receives a critical hit.</summary>
    public static bool RollCritical(TechData tech, BattleStats atk, BattleStats def, BattleFighter atkFighter, BattleFighter defFighter, int rngValue)
    {
        int chance = BaseChance(tech, atk, atkFighter) + FamilyChance(tech, atk, def);
        chance += StatusCriticalBonus(defFighter);
        return (rngValue & 0x7f) < chance;
    }

    /// <summary>Returns whether a magic technique receives a critical hit.</summary>
    public static bool RollMagicCritical(TechData tech, BattleStats def, BattleFighter defFighter, int rngValue)
    {
        int value = Math.Min(tech.ElementPower * 100 / def.Resist[tech.Element - BattleConst.ElementFirst], 0x40);
        int chance = value + 4;
        if (tech.Family >= BattleConst.FamilyFirst && tech.Family == def.Family)
        {
            chance = value + 0x40;
        }

        chance += StatusCriticalBonus(defFighter);
        return (rngValue & 0x7f) < chance;
    }

    /// <summary>
    /// The chance before family and status bonuses: 4, plus the technique's power when its effect
    /// is the critical effect; an effect-less technique takes the attacker's critical bonus
    /// instead (doubled while the fighter is in its special state).
    /// </summary>
    /// <param name="tech">The technique used.</param>
    /// <param name="atk">The attacker's resolved stats.</param>
    /// <param name="atkFighter">The attacker, for its special state.</param>
    /// <returns>The chance out of 128.</returns>
    private static int BaseChance(TechData tech, BattleStats atk, BattleFighter atkFighter)
    {
        if (tech.Effect >= BattleConst.TechEffectFirst)
        {
            return tech.Effect == BattleConst.TechEffectCritical ? 4 + tech.EffectPower : 4;
        }

        if (atk.CriticalBonus == 0)
        {
            return 4;
        }

        return atkFighter.Special ? (atk.CriticalBonus * 2) + 4 : atk.CriticalBonus + 4;
    }

    /// <summary>
    /// The family bonus: 0x3C when the technique's family matches the defender, or 0x10 when a
    /// family-less technique is used with a weapon of the defender's family.
    /// </summary>
    /// <param name="tech">The technique used.</param>
    /// <param name="atk">The attacker's resolved stats, for its weapons.</param>
    /// <param name="def">The defender's resolved stats, for its family.</param>
    /// <returns>The bonus out of 128.</returns>
    private static int FamilyChance(TechData tech, BattleStats atk, BattleStats def)
    {
        if (tech.Family >= BattleConst.FamilyFirst)
        {
            return tech.Family == def.Family ? 0x3c : 0;
        }

        return atk.HasFamilyWeapon(def.Family) ? 0x10 : 0;
    }

    /// <summary>
    /// Extra chance against a disabled defender: an eighth of its paralysis and sleep counters
    /// and half of its confusion counter, each only while the matching status flag is set.
    /// </summary>
    /// <param name="fighter">The defender.</param>
    /// <returns>The bonus out of 128.</returns>
    private static int StatusCriticalBonus(BattleFighter fighter)
    {
        int bonus = 0;
        if ((fighter.Flags & FighterFlags.Paralyzed) != 0)
        {
            bonus += fighter.Paralysis >> 3;
        }

        if ((fighter.Flags & FighterFlags.Asleep) != 0)
        {
            bonus += fighter.Sleep >> 3;
        }

        if ((fighter.Flags & FighterFlags.Confused) != 0)
        {
            bonus += fighter.Confusion >> 1;
        }

        return bonus;
    }
}
