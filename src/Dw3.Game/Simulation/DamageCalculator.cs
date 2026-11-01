namespace Dw3Production.Simulation;

/// <summary>Provides deterministic integer calculations for battle damage and turn timing.</summary>
public static class DamageCalculator
{
    /// <summary>Maps each element to the element that counters it.</summary>
    public static readonly int[] OpposedElements = [0, 0, 4, 2, 5, 3, 7, 8, 6];

    /// <summary>Returns the adjustment caused by the active battle element.</summary>
    public static int GetElementBoost(int value, int element, ElementBoost boost)
    {
        if (boost.Element < BattleConst.ElementFirst)
        {
            return 0;
        }

        if (element == boost.Element)
        {
            return value * boost.Amount / 128;
        }

        return OpposedElements[element] == boost.Element ? -(value * boost.Amount / 256) : 0;
    }

    /// <summary>Returns the tick delay before the acting side receives another turn.</summary>
    public static int GetTurnDelay(int ownSpeed, int otherSpeed)
    {
        ownSpeed = Math.Max(ownSpeed, 1);
        otherSpeed = Math.Max(otherSpeed, 1);
        int product = ownSpeed * otherSpeed;
        int root = 999;
        for (var iteration = 0; iteration < 10; iteration++)
        {
            root = (root + (product / root)) / 2;
        }

        return Math.Clamp(1000 * otherSpeed / root, 707, 1414);
    }

    /// <summary>Applies elemental, family, critical, and defensive adjustments to physical damage.</summary>
    public static int AdjustDamage(TechData tech, BattleStats user, BattleStats target, int value, ElementBoost boost, int critRoll)
    {
        int result = value;
        result += GetElementBoost(result, tech.Element, boost);
        result += FamilyBonus(tech, user, target, result);

        if (user.DamageBonus != 0)
        {
            result += result * user.DamageBonus / 64;
        }

        result += ElementPowerBonus(tech, user, target, result);

        if (tech.Effect < BattleConst.TechEffectFirst && user.TripleHit != 0 && !IsScriptedElementTech(tech))
        {
            result = result * 4 / 10;
        }

        return FinishDamage(result, value, target.DamageCut, critRoll);
    }

    /// <summary>Computes physical technique damage for either battle side.</summary>
    public static int ComputeDamage(TechData tech, BattleStats user, BattleStats target, bool enemyAttacker, int strength, ElementBoost boost, int critRoll)
    {
        int value = enemyAttacker
            ? tech.Power * strength / 16 * user.Stats[BattleStat.Attack] / target.Stats[BattleStat.Defense]
            : tech.Power * user.Stats[BattleStat.Attack] / target.Stats[BattleStat.Defense];
        return AdjustDamage(tech, user, target, value, boost, critRoll);
    }

    /// <summary>Computes magic technique damage for either battle side.</summary>
    public static int ComputeMagicDamage(TechData tech, BattleStats user, BattleStats target, bool enemyAttacker, int strength, ElementBoost boost, int critRoll)
    {
        int basePower = enemyAttacker ? tech.Power * strength / 16 : tech.Power;
        int power = basePower * ((user.Stats[BattleStat.Spirit] * 50 / target.Stats[BattleStat.Spirit]) + 50) / 100;
        power = Math.Clamp(power, basePower / 2, basePower * 2);
        int result = power + GetElementBoost(power, tech.Element, boost);

        if (tech.Element >= BattleConst.ElementFirst)
        {
            result = ApplyMagicResistance(result, target.Resist[tech.Element - BattleConst.ElementFirst]);
        }

        if (tech.Family >= BattleConst.FamilyFirst && tech.Family == target.Family)
        {
            result += result / 2;
        }

        return FinishDamage(result, power, target.DamageCut, critRoll);
    }

    /// <summary>Computes an enemy counterattack from the damage it received.</summary>
    public static int ComputeCounterDamage(TechData tech, BattleStats user, BattleStats target, int receivedDamage, ElementBoost boost, int critRoll, bool special = false)
    {
        int value = receivedDamage * tech.EffectPower / (special ? 32 : 64);
        return AdjustDamage(tech, user, target, value, boost, critRoll);
    }

    /// <summary>Scales an enemy table entry into the resolved stats used for a battle.</summary>
    public static BattleStats ComputeEnemyStats(BattleStats entry, int level, int strength)
    {
        var result = new BattleStats { Level = level, Family = entry.Family };
        for (var index = 0; index < result.Stats.Length; index++)
        {
            result.Stats[index] = entry.Stats[index] * strength / 16;
        }

        for (var index = 0; index < result.Resist.Length; index++)
        {
            result.Resist[index] = entry.Resist[index];
        }

        return result;
    }

    /// <summary>
    /// The family bonus of a physical hit: +50 % when the technique's own family matches the
    /// target, or, for a family-less technique, when one of the user's weapons matches it.
    /// </summary>
    /// <param name="tech">The technique used.</param>
    /// <param name="user">The attacker's resolved stats.</param>
    /// <param name="target">The defender's resolved stats.</param>
    /// <param name="damage">The damage so far.</param>
    /// <returns>The damage to add.</returns>
    private static int FamilyBonus(TechData tech, BattleStats user, BattleStats target, int damage)
    {
        bool matches = tech.Family >= BattleConst.FamilyFirst
            ? tech.Family == target.Family
            : user.HasFamilyWeapon(target.Family);
        return matches ? damage / 2 : 0;
    }

    /// <summary>
    /// The elemental power bonus of a physical hit, scaled by the target's resistance to that
    /// element: the technique's element when it has one, otherwise the user's accessory element
    /// (except for scripts 11 and 12, which never take the accessory's).
    /// </summary>
    /// <param name="tech">The technique used.</param>
    /// <param name="user">The attacker's resolved stats.</param>
    /// <param name="target">The defender's resolved stats; its resistance is the divisor.</param>
    /// <param name="damage">The damage so far.</param>
    /// <returns>The damage to add.</returns>
    private static int ElementPowerBonus(TechData tech, BattleStats user, BattleStats target, int damage)
    {
        if (tech.Element >= BattleConst.ElementFirst)
        {
            return damage * tech.ElementPower * 2 / target.Resist[tech.Element - BattleConst.ElementFirst];
        }

        if (!IsScriptedElementTech(tech) && user.Element != 0)
        {
            return damage * user.ElementPower * 2 / target.Resist[user.Element - BattleConst.ElementFirst];
        }

        return 0;
    }

    /// <summary>Whether the technique runs script 11 or 12, which ignore the accessory element and the triple hit.</summary>
    /// <param name="tech">The technique used.</param>
    /// <returns><see langword="true"/> for scripts 11 and 12.</returns>
    private static bool IsScriptedElementTech(TechData tech) => tech.Script is >= 11 and <= 12;

    /// <summary>
    /// Magic damage against an elemental resistance (100 is neutral): below 100 the damage
    /// rises steeply, from 300 up it falls towards immunity, in between it falls gently.
    /// </summary>
    /// <param name="damage">The magic damage so far.</param>
    /// <param name="resist">The target's resistance to the technique's element.</param>
    /// <returns>The adjusted damage.</returns>
    private static int ApplyMagicResistance(int damage, int resist)
    {
        if (resist < 100)
        {
            return damage * (400 - (resist * 3)) / 100;
        }

        return resist >= 300
            ? damage * (65 - (resist / 20)) / 100
            : damage * (125 - (resist / 4)) / 100;
    }

    /// <summary>
    /// The steps every hit ends with: the critical bonus (a roll of -1 means no critical), the
    /// target's flat damage cut, the cap at five times the unadjusted value, and the 9999 display cap.
    /// </summary>
    /// <param name="damage">The adjusted damage.</param>
    /// <param name="unadjusted">The damage before any adjustment, the base of the 5x cap.</param>
    /// <param name="damageCut">The target's flat damage cut.</param>
    /// <param name="critRoll">The critical strength, or -1.</param>
    /// <returns>The final damage, 0..9999.</returns>
    private static int FinishDamage(int damage, int unadjusted, int damageCut, int critRoll)
    {
        if (critRoll >= 0)
        {
            damage += damage * (critRoll + 0x20) / 64;
        }

        damage = ApplyDamageCut(damage, damageCut);
        if (damage > unadjusted * 5)
        {
            damage = unadjusted * 5;
        }

        return damage >= 10000 ? 9999 : damage;
    }

    private static int ApplyDamageCut(int value, int damageCut)
    {
        if (damageCut == 0)
        {
            return value;
        }

        return Math.Max(value - damageCut, 0);
    }
}
