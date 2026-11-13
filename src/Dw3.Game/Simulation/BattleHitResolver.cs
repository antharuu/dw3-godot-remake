namespace Dw3Production.Simulation;

/// <summary>
/// Resolves one technique's hit check and damage. The disc rolls hit first, then still computes
/// critical and damage on a miss, so the shared random stream remains aligned while callers suppress
/// damage and on-hit reactions for an unlanded result.
/// </summary>
/// <param name="boost">The battlefield's element boost.</param>
/// <param name="allowCritical">When <see langword="false"/> no critical is rolled and no critical random value is consumed.</param>
/// <param name="rng">The battle's random source.</param>
public sealed class BattleHitResolver(ElementBoost boost, bool allowCritical, IBattleRng rng)
{
    /// <summary>Resolves a basic physical attack, including its landing check.</summary>
    /// <param name="tech">The technique used.</param>
    /// <param name="attacker">The fighter that uses it.</param>
    /// <param name="target">The fighter it targets.</param>
    /// <param name="enemyAttacker">Whether the attacker is on the enemy side.</param>
    /// <returns>The landing and damage result.</returns>
    public BattleHitResult Basic(TechData tech, BattleFighter attacker, BattleFighter target, bool enemyAttacker)
    {
        attacker.RefreshStats();
        target.RefreshStats();
        bool landed = BattleHitRules.RollPhysical(tech, attacker.Stats, target.Stats, enemyAttacker, rng.NextValue());
        int crit = RollCrit(attacker, target, tech);
        int damage = DamageCalculator.ComputeDamage(tech, attacker.Stats, target.Stats, enemyAttacker, attacker.Strength, boost, crit);
        return new BattleHitResult(landed, damage);
    }

    /// <summary>Resolves a physical or magic technique, including its landing check.</summary>
    /// <param name="tech">The technique used.</param>
    /// <param name="attacker">The fighter that uses it.</param>
    /// <param name="target">The fighter it targets.</param>
    /// <param name="enemyAttacker">Whether the attacker is on the enemy side.</param>
    /// <returns>The landing and damage result.</returns>
    public BattleHitResult Technique(TechData tech, BattleFighter attacker, BattleFighter target, bool enemyAttacker)
        => Technique(tech, tech, attacker, target, enemyAttacker);

    /// <summary>Resolves a technique with separate source records for its accuracy and magic-damage fields.</summary>
    /// <param name="accuracyTech">Technique supplying icon, accuracy, and magic critical fields.</param>
    /// <param name="damageTech">Technique supplying the magic damage fields.</param>
    /// <param name="attacker">The fighter that uses the technique.</param>
    /// <param name="target">The fighter it targets.</param>
    /// <param name="enemyAttacker">Whether the attacker is on the enemy side.</param>
    /// <returns>The landing and damage result.</returns>
    public BattleHitResult Technique(TechData accuracyTech, TechData damageTech, BattleFighter attacker, BattleFighter target, bool enemyAttacker)
    {
        if (accuracyTech.Icon != BattleConst.TechMagic)
        {
            return Basic(damageTech, attacker, target, enemyAttacker);
        }

        attacker.RefreshStats();
        target.RefreshStats();
        bool landed = BattleHitRules.RollMagic(accuracyTech, attacker.Stats, target.Stats, rng.NextValue());
        int crit = RollMagicCrit(target, accuracyTech);
        int damage = DamageCalculator.ComputeMagicDamage(damageTech, attacker.Stats, target.Stats, enemyAttacker, attacker.Strength, boost, crit);
        return new BattleHitResult(landed, damage);
    }

    /// <summary>Resolves direct item magic without an ordinary technique accuracy gate or MP payment.</summary>
    /// <param name="tech">Prepared item technique supplying magic damage and critical fields.</param>
    /// <param name="attacker">Active partner that supplies freshly resolved spirit.</param>
    /// <param name="target">Active enemy that supplies freshly resolved resistance.</param>
    /// <returns>The direct magic damage after the source critical policy.</returns>
    public int DirectItemMagic(TechData tech, BattleFighter attacker, BattleFighter target)
    {
        attacker.RefreshStats();
        target.RefreshStats();
        int crit = RollMagicCrit(target, tech);
        return DamageCalculator.ComputeMagicDamage(tech, attacker.Stats, target.Stats, enemyAttacker: false, attacker.Strength, boost, crit);
    }

    /// <summary>The damage of an enemy counter-attack, a share of the damage the enemy just received.</summary>
    /// <param name="tech">The counter technique; its effect power sets the share in 64ths.</param>
    /// <param name="attacker">The countering enemy.</param>
    /// <param name="target">The partner that hit it.</param>
    /// <param name="receivedDamage">The damage of the hit that triggered it.</param>
    /// <returns>The landing and damage result.</returns>
    public BattleHitResult Counter(TechData tech, BattleFighter attacker, BattleFighter target, int receivedDamage)
    {
        attacker.RefreshStats();
        target.RefreshStats();
        bool landed = BattleHitRules.RollPhysical(tech, attacker.Stats, target.Stats, enemyAttacker: true, rng.NextValue());
        if (!landed)
        {
            return new BattleHitResult(Landed: false, Damage: 0);
        }

        int crit = RollCrit(attacker, target, tech);
        int damage = DamageCalculator.ComputeCounterDamage(tech, attacker.Stats, target.Stats, receivedDamage, boost, crit);
        return new BattleHitResult(Landed: true, damage);
    }

    /// <summary>Rolls a physical critical and returns its strength, or -1 when it does not occur.</summary>
    /// <param name="attacker">The fighter using the technique.</param>
    /// <param name="target">The fighter the technique targets.</param>
    /// <param name="tech">The technique whose physical critical rule is applied.</param>
    /// <returns>The critical strength, or -1 when no critical applies.</returns>
    private int RollCrit(BattleFighter attacker, BattleFighter target, TechData tech)
    {
        if (!allowCritical)
        {
            return -1;
        }

        bool crit = CriticalHitRules.RollCritical(tech, attacker.Stats, target.Stats, attacker, target, rng.NextValue());
        return crit ? rng.NextValue() & 0x3f : -1;
    }

    /// <summary>Rolls a magic critical and returns its strength, or -1 when it does not occur.</summary>
    /// <param name="target">The fighter the technique targets.</param>
    /// <param name="tech">The technique whose magic critical rule is applied.</param>
    /// <returns>The critical strength, or -1 when no critical applies.</returns>
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
