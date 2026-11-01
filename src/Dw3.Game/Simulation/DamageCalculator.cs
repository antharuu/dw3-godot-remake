namespace Dw3Production.Simulation;

/// <summary>Supplies battle random values to the simulation.</summary>
public interface BattleRng
{
    /// <summary>Returns the next raw random value for a battle decision.</summary>
    int Next();
}

/// <summary>Provides deterministic integer calculations for battle damage, timing, and chance rolls.</summary>
public static class DamageCalculator
{
    /// <summary>Maps each element to the element that counters it.</summary>
    public static readonly int[] OpposedElements = [0, 0, 4, 2, 5, 3, 7, 8, 6];

    /// <summary>Returns the adjustment caused by the active battle element.</summary>
    public static int GetElementBoost(int value, int element, int boostElement, int boostAmount)
    {
        if (boostElement < BattleConst.ElementFirst) return 0;
        if (element == boostElement) return value * boostAmount / 128;
        return OpposedElements[element] == boostElement ? -(value * boostAmount / 256) : 0;
    }

    /// <summary>Returns the tick delay before the acting side receives another turn.</summary>
    public static int GetTurnDelay(int ownSpeed, int otherSpeed)
    {
        ownSpeed = Math.Max(ownSpeed, 1);
        otherSpeed = Math.Max(otherSpeed, 1);
        int product = ownSpeed * otherSpeed;
        int root = 999;
        for (var iteration = 0; iteration < 10; iteration++)
            root = (root + product / root) / 2;
        return Math.Clamp(1000 * otherSpeed / root, 707, 1414);
    }

    /// <summary>Returns whether a player's attempt to leave a battle succeeds.</summary>
    public static bool TestRunAway(int runAttempts, BattleStats atk, BattleStats def, bool blockRunAway, int atkFlags, int rngValue)
    {
        if (blockRunAway || (atkFlags & (FighterFlags.Asleep | FighterFlags.NoSwitch)) != 0) return false;
        int chance = (runAttempts + 1) * 8;
        if ((atkFlags & FighterFlags.Paralyzed) != 0) chance >>= 1;
        if (atk.Level > def.Level) chance += atk.Level - def.Level;
        if (atk.Stats[BattleStat.Speed] > def.Stats[BattleStat.Speed])
            chance += (atk.Stats[BattleStat.Speed] - def.Stats[BattleStat.Speed]) / 10;
        chance += atk.RunAwayBonus;
        return (rngValue & 0x7f) < chance;
    }

    /// <summary>Returns whether an enemy attempt to leave a battle succeeds.</summary>
    public static bool TestEnemyRunAway(BattleStats atk, BattleStats def, int atkFlags, int rngValue)
    {
        if ((atkFlags & FighterFlags.Asleep) != 0) return false;
        int chance = def.RunAwayGuard != 0 ? 0x20 : 0x40;
        if ((atkFlags & FighterFlags.Paralyzed) != 0) chance >>= 1;
        chance += (atk.Stats[BattleStat.Speed] - def.Stats[BattleStat.Speed]) / 10;
        return (rngValue & 0x7f) < chance;
    }

    /// <summary>Applies elemental, family, critical, and defensive adjustments to physical damage.</summary>
    public static int AdjustDamage(TechData tech, BattleStats user, BattleStats target, int value, int boostElement, int boostAmount, int critRoll)
    {
        int result = value;
        result += GetElementBoost(result, tech.Element, boostElement, boostAmount);

        if (tech.Family >= BattleConst.FamilyFirst)
        {
            if (tech.Family == target.Family) result += result / 2;
        }
        else if (HasFamilyWeapon(user, target.Family))
        {
            result += result / 2;
        }

        if (user.DamageBonus != 0) result += result * user.DamageBonus / 64;

        if (tech.Element >= BattleConst.ElementFirst)
            result += result * tech.ElementPower * 2 / target.Resist[tech.Element - BattleConst.ElementFirst];
        else if ((tech.Script < 11 || tech.Script > 12) && user.Element != 0)
            result += result * user.ElementPower * 2 / target.Resist[user.Element - BattleConst.ElementFirst];

        if (tech.Effect < BattleConst.TechEffectFirst && user.TripleHit != 0 && (tech.Script < 11 || tech.Script > 12))
            result = result * 4 / 10;
        if (critRoll >= 0) result += result * (critRoll + 0x20) / 64;
        result = ApplyDamageCut(result, target.DamageCut);
        if (result > value * 5) result = value * 5;
        return result >= 10000 ? 9999 : result;
    }

    /// <summary>Computes physical technique damage for either battle side.</summary>
    public static int ComputeDamage(TechData tech, BattleStats user, BattleStats target, bool enemyAttacker, int strength, int boostElement, int boostAmount, int critRoll)
    {
        int value = enemyAttacker
            ? tech.Power * strength / 16 * user.Stats[BattleStat.Attack] / target.Stats[BattleStat.Defense]
            : tech.Power * user.Stats[BattleStat.Attack] / target.Stats[BattleStat.Defense];
        return AdjustDamage(tech, user, target, value, boostElement, boostAmount, critRoll);
    }

    /// <summary>Computes magic technique damage for either battle side.</summary>
    public static int ComputeMagicDamage(TechData tech, BattleStats user, BattleStats target, bool enemyAttacker, int strength, int boostElement, int boostAmount, int critRoll)
    {
        int basePower = enemyAttacker ? tech.Power * strength / 16 : tech.Power;
        int power = basePower * (user.Stats[BattleStat.Spirit] * 50 / target.Stats[BattleStat.Spirit] + 50) / 100;
        power = Math.Clamp(power, basePower / 2, basePower * 2);
        int result = power + GetElementBoost(power, tech.Element, boostElement, boostAmount);

        if (tech.Element >= BattleConst.ElementFirst)
        {
            int resist = target.Resist[tech.Element - BattleConst.ElementFirst];
            result = resist < 100
                ? result * (400 - resist * 3) / 100
                : resist >= 300
                    ? result * (65 - resist / 20) / 100
                    : result * (125 - resist / 4) / 100;
        }

        if (tech.Family >= BattleConst.FamilyFirst && tech.Family == target.Family) result += result / 2;
        if (critRoll >= 0) result += result * (critRoll + 0x20) / 64;
        result = ApplyDamageCut(result, target.DamageCut);
        if (result > power * 5) result = power * 5;
        return result >= 10000 ? 9999 : result;
    }

    /// <summary>Computes an enemy counterattack from the damage it received.</summary>
    public static int ComputeCounterDamage(TechData tech, BattleStats user, BattleStats target, int receivedDamage, int boostElement, int boostAmount, int critRoll, bool special = false)
    {
        int value = receivedDamage * tech.EffectPower / (special ? 32 : 64);
        return AdjustDamage(tech, user, target, value, boostElement, boostAmount, critRoll);
    }

    /// <summary>Scales an enemy table entry into the resolved stats used for a battle.</summary>
    public static BattleStats ComputeEnemyStats(BattleStats entry, int level, int strength)
    {
        var result = new BattleStats { Level = level, Family = entry.Family };
        for (var index = 0; index < result.Stats.Length; index++) result.Stats[index] = entry.Stats[index] * strength / 16;
        for (var index = 0; index < result.Resist.Length; index++) result.Resist[index] = entry.Resist[index];
        return result;
    }

    /// <summary>Returns whether a physical technique receives a critical hit.</summary>
    public static bool RollCritical(TechData tech, BattleStats atk, BattleStats def, BattleFighter atkFighter, BattleFighter defFighter, int rngValue)
    {
        int chance = 4;
        if (tech.Effect >= BattleConst.TechEffectFirst)
        {
            if (tech.Effect == BattleConst.TechEffectCritical) chance += tech.EffectPower;
        }
        else if (atk.CriticalBonus != 0)
        {
            chance = atkFighter.Special ? atk.CriticalBonus * 2 + 4 : atk.CriticalBonus + 4;
        }

        if (tech.Family >= BattleConst.FamilyFirst)
        {
            if (tech.Family == def.Family) chance += 0x3c;
        }
        else if (HasFamilyWeapon(atk, def.Family))
        {
            chance += 0x10;
        }

        chance += StatusCriticalBonus(defFighter);
        return (rngValue & 0x7f) < chance;
    }

    /// <summary>Returns whether a magic technique receives a critical hit.</summary>
    public static bool RollMagicCritical(TechData tech, BattleStats def, BattleFighter defFighter, int rngValue)
    {
        int value = Math.Min(tech.ElementPower * 100 / def.Resist[tech.Element - BattleConst.ElementFirst], 0x40);
        int chance = value + 4;
        if (tech.Family >= BattleConst.FamilyFirst && tech.Family == def.Family) chance = value + 0x40;
        chance += StatusCriticalBonus(defFighter);
        return (rngValue & 0x7f) < chance;
    }

    private static bool HasFamilyWeapon(BattleStats user, int family)
    {
        foreach (int weaponFamily in user.WeaponFamilies)
            if (weaponFamily >= BattleConst.FamilyFirst && weaponFamily == family) return true;
        return false;
    }

    private static int ApplyDamageCut(int value, int damageCut)
    {
        if (damageCut == 0) return value;
        return Math.Max(value - damageCut, 0);
    }

    private static int StatusCriticalBonus(BattleFighter fighter)
    {
        int bonus = 0;
        if ((fighter.Flags & FighterFlags.Paralyzed) != 0) bonus += fighter.Paralysis >> 3;
        if ((fighter.Flags & FighterFlags.Asleep) != 0) bonus += fighter.Sleep >> 3;
        if ((fighter.Flags & FighterFlags.Confused) != 0) bonus += fighter.Confusion >> 1;
        return bonus;
    }
}
