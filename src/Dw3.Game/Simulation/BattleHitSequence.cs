namespace Dw3Production.Simulation;

/// <summary>Records and resolves the ordered hit rolls of one action without letting a knock-out roll further hits.</summary>
internal sealed class BattleHitSequence
{
    private readonly List<BattleHitEvent> _events = [];

    /// <summary>Gets a value indicating whether at least one non-counter hit in the current sequence landed, regardless of integer damage.</summary>
    /// <remarks>
    /// The gate for a technique's status effect is a landed hit, not positive aggregate damage.
    /// A hit whose integer damage rounds to zero still landed and must still apply its effect.
    /// </remarks>
    public bool AnyActionLanded
    {
        get
        {
            foreach (BattleHitEvent hit in _events)
            {
                if (hit.Landed && !hit.IsCounter)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets the ordered events from the most recently started action and any appended counter-reaction.</summary>
    public IReadOnlyList<BattleHitEvent> Events => _events;

    /// <summary>Resolves one action or counter sequence against its original target.</summary>
    /// <param name="side">Side rolling the hits.</param>
    /// <param name="tech">Technique that determines the count and is traced with every event.</param>
    /// <param name="attackerStats">The attacker's resolved stats, used for the triple-hit count. Pass null for a counter-attack.</param>
    /// <param name="target">Fixed target for the sequence.</param>
    /// <param name="isCounter">Whether events append to an action as its counter-reaction.</param>
    /// <param name="resolve">Function resolving one hit's accuracy and raw damage.</param>
    /// <param name="checkAndReportOutcome">Updates the battle outcome and returns whether the battle has now ended.</param>
    /// <returns>Total HP actually removed.</returns>
    public int Resolve(BattleSide side, TechData tech, BattleStats? attackerStats, BattleFighter target, bool isCounter, Func<BattleHitResult> resolve, Func<bool> checkAndReportOutcome)
    {
        if (!isCounter)
        {
            _events.Clear();
        }

        int damage = 0;
        for (int index = 0; index < HitCountFor(tech, attackerStats, isCounter); index++)
        {
            if (target.IsKnockedOut || checkAndReportOutcome())
            {
                break;
            }

            damage += ResolveHit(side, tech, target, isCounter, index, resolve());
            _ = checkAndReportOutcome();
        }

        return damage;
    }

    /// <summary>Returns the source-defined count for multi-hit, double-magic and triple-hit techniques, otherwise one.</summary>
    /// <param name="tech">Technique whose effect selects the sequence rule.</param>
    /// <param name="attackerStats">The attacker's resolved stats, used to check the triple-hit equipment modifier. Null for a counter-attack (counters are always single-hit).</param>
    /// <param name="isCounter">Whether the sequence is a counter-reaction; counter-attacks are always single-hit regardless of the attacker's equipment.</param>
    /// <returns>At least one hit.</returns>
    /// <remarks>
    /// <para><see cref="BattleStats.TripleHit"/> is set by equipment. When non-zero, a plain damaging
    /// technique (effect below <see cref="BattleConst.TechEffectFirst"/> and not a scripted element
    /// technique) hits three times at 4/10 damage per hit. <see cref="DamageCalculator"/> applies
    /// the per-hit cut independently; this method wires the matching count expansion.
    /// Counter-attacks and techniques with a special effect are excluded from the expansion.</para>
    /// </remarks>
    private static int HitCountFor(TechData tech, BattleStats? attackerStats, bool isCounter) => tech.Effect switch
    {
        BattleTechniqueEffect.MultiHit => Math.Max(tech.HitCount, 1),
        BattleTechniqueEffect.DoubleMagic => 2,
        _ => !isCounter && IsTripleHitPlain(tech, attackerStats) ? 3 : 1,
    };

    /// <summary>Returns <see langword="true"/> when the triple-hit equipment modifier applies to this technique.</summary>
    /// <param name="tech">Technique being used.</param>
    /// <param name="attackerStats">The attacker's resolved stats.</param>
    /// <returns><see langword="true"/> for a plain damaging technique with the attacker's triple-hit set and no scripted element override.</returns>
    private static bool IsTripleHitPlain(TechData tech, BattleStats? attackerStats) =>
        tech.Effect < BattleConst.TechEffectFirst
        && (attackerStats?.TripleHit ?? 0) != 0
        && tech.Script is not (>= 11 and <= 12);

    /// <summary>Applies and records one resolved hit.</summary>
    /// <param name="side">Side rolling the hit.</param>
    /// <param name="tech">Technique whose effect is traced with the hit.</param>
    /// <param name="target">Fighter receiving the hit.</param>
    /// <param name="isCounter">Whether the hit is a counter-reaction.</param>
    /// <param name="index">Zero-based sequence index.</param>
    /// <param name="result">Resolved accuracy and raw damage.</param>
    /// <returns>Damage actually removed from HP.</returns>
    private int ResolveHit(BattleSide side, TechData tech, BattleFighter target, bool isCounter, int index, BattleHitResult result)
    {
        int damage = result.Landed ? Math.Min(result.Damage, target.Hp) : 0;
        if (damage > 0)
        {
            target.TakeDamage(damage);
        }

        _events.Add(new BattleHitEvent(side, index, result.Landed, damage, tech.Effect, isCounter));
        return damage;
    }
}
