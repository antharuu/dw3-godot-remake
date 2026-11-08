namespace Dw3Production.Simulation;

/// <summary>Records and resolves the ordered hit rolls of one action without letting a knock-out roll further hits.</summary>
internal sealed class BattleHitSequence
{
    private readonly List<BattleHitEvent> _events = [];

    /// <summary>Gets the ordered events from the most recently started action and any appended counter-reaction.</summary>
    public IReadOnlyList<BattleHitEvent> Events => _events;

    /// <summary>Resolves one action or counter sequence against its original target.</summary>
    /// <param name="side">Side rolling the hits.</param>
    /// <param name="tech">Technique that determines the count.</param>
    /// <param name="target">Fixed target for the sequence.</param>
    /// <param name="isCounter">Whether events append to an action as its counter-reaction.</param>
    /// <param name="resolve">Function resolving one hit's accuracy and raw damage.</param>
    /// <param name="checkOutcome">Updates the battle outcome after a landed hit.</param>
    /// <param name="hasOutcome">Reports whether the battle has ended.</param>
    /// <returns>Total HP actually removed.</returns>
    public int Resolve(BattleSide side, TechData tech, BattleFighter target, bool isCounter, Func<BattleHitResult> resolve, Action checkOutcome, Func<bool> hasOutcome)
    {
        if (!isCounter)
        {
            _events.Clear();
        }

        int damage = 0;
        for (int index = 0; index < HitCountFor(tech); index++)
        {
            if (target.IsKnockedOut || hasOutcome())
            {
                break;
            }

            damage += ResolveHit(side, tech, target, isCounter, index, resolve());
            checkOutcome();
        }

        return damage;
    }

    /// <summary>Returns the source-defined count for multi-hit and double-magic techniques, otherwise one.</summary>
    /// <param name="tech">Technique whose effect selects the sequence rule.</param>
    /// <returns>At least one hit.</returns>
    private static int HitCountFor(TechData tech) => tech.Effect switch
    {
        BattleTechniqueEffect.MultiHit => Math.Max(tech.HitCount, 1),
        BattleTechniqueEffect.DoubleMagic => 2,
        _ => 1,
    };

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
