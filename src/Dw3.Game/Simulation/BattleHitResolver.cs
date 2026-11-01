namespace Dw3Production.Simulation;

/// <summary>
/// Resolves the damage of one technique landing on one fighter: it rolls the critical hit
/// (when criticals are enabled), picks the physical or magic formula and applies the
/// battlefield's element boost. Both sides use it, so a player hit and an enemy hit consume
/// the random values in the same order.
/// </summary>
/// <param name="boost">The battlefield's element boost.</param>
/// <param name="allowCritical">When <see langword="false"/> no critical is rolled and no random value is consumed.</param>
/// <param name="rng">The battle's random source.</param>
public sealed class BattleHitResolver(ElementBoost boost, bool allowCritical, IBattleRng rng)
{
    /// <summary>
    /// The damage of a basic attack, always a physical hit. An enemy attacker's technique power
    /// is scaled by its strength in 16ths, a partner's is not.
    /// </summary>
    /// <param name="tech">The technique used.</param>
    /// <param name="attacker">The fighter that uses it.</param>
    /// <param name="target">The fighter it lands on.</param>
    /// <param name="enemyAttacker">Whether the attacker is on the enemy side.</param>
    /// <returns>The HP to take off the target, 0..9999.</returns>
    public int Basic(TechData tech, BattleFighter attacker, BattleFighter target, bool enemyAttacker)
    {
        int crit = RollCrit(attacker, target, tech);
        return DamageCalculator.ComputeDamage(tech, attacker.Stats, target.Stats, enemyAttacker, attacker.Strength, boost, crit);
    }

    /// <summary>
    /// The damage of a technique picked from a menu or a battle table: a magic technique
    /// (<see cref="BattleConst.TechMagic"/> icon) uses the spirit formula and the magic critical
    /// roll, any other technique is a physical hit.
    /// </summary>
    /// <param name="tech">The technique used; its MP cost is the caller's concern.</param>
    /// <param name="attacker">The fighter that uses it.</param>
    /// <param name="target">The fighter it lands on.</param>
    /// <param name="enemyAttacker">Whether the attacker is on the enemy side.</param>
    /// <returns>The HP to take off the target, 0..9999.</returns>
    public int Technique(TechData tech, BattleFighter attacker, BattleFighter target, bool enemyAttacker)
    {
        if (tech.Icon != BattleConst.TechMagic)
        {
            return Basic(tech, attacker, target, enemyAttacker);
        }

        int crit = RollMagicCrit(target, tech);
        return DamageCalculator.ComputeMagicDamage(tech, attacker.Stats, target.Stats, enemyAttacker, attacker.Strength, boost, crit);
    }

    /// <summary>The damage of an enemy counter-attack, a share of the damage the enemy just received.</summary>
    /// <param name="tech">The counter technique; its effect power sets the share in 64ths.</param>
    /// <param name="attacker">The countering enemy.</param>
    /// <param name="target">The partner that hit it.</param>
    /// <param name="receivedDamage">The damage of the hit that triggered the counter.</param>
    /// <returns>The HP to take off the target, 0..9999.</returns>
    public int Counter(TechData tech, BattleFighter attacker, BattleFighter target, int receivedDamage)
    {
        int crit = RollCrit(attacker, target, tech);
        return DamageCalculator.ComputeCounterDamage(tech, attacker.Stats, target.Stats, receivedDamage, boost, crit);
    }

    /// <summary>
    /// Rolls a physical critical: one random value decides it, a second one (0..63) sets its
    /// strength. Returns -1, the formulas' "no critical", when it misses or criticals are off.
    /// </summary>
    /// <param name="attacker">The fighter that uses the technique.</param>
    /// <param name="target">The fighter it lands on.</param>
    /// <param name="tech">The technique used.</param>
    /// <returns>The critical strength 0..63, or -1.</returns>
    private int RollCrit(BattleFighter attacker, BattleFighter target, TechData tech)
    {
        if (!allowCritical)
        {
            return -1;
        }

        bool crit = CriticalHitRules.RollCritical(tech, attacker.Stats, target.Stats, attacker, target, rng.NextValue());
        return crit ? rng.NextValue() & 0x3F : -1;
    }

    /// <summary>Rolls a magic critical like <see cref="RollCrit"/>, with a strength of 0..64.</summary>
    /// <param name="target">The fighter the technique lands on.</param>
    /// <param name="tech">The magic technique used.</param>
    /// <returns>The critical strength 0..64, or -1.</returns>
    private int RollMagicCrit(BattleFighter target, TechData tech)
    {
        if (!allowCritical)
        {
            return -1;
        }

        bool crit = CriticalHitRules.RollMagicCritical(tech, target.Stats, target, rng.NextValue());
        return crit ? rng.NextValue() % 65 : -1;
    }
}
