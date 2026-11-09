namespace Dw3Production.Simulation;

/// <summary>Queues and resolves the enemy-only forced return of the active partner to its base form.</summary>
public static class BattleDedigivolveResolver
{
    /// <summary>Tests the source form boundary and queues an accepted effect-7 transition ahead of ordinary turns.</summary>
    /// <param name="attackerSide">Side attempting the effect.</param>
    /// <param name="targetSlot">Active player slot captured by the action.</param>
    /// <param name="target">Active partner targeted by the action.</param>
    /// <param name="baseDigimonIds">Every registered partner base identity, including partners absent from this battle.</param>
    /// <param name="chance">Extracted chance in 128ths.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <param name="scheduler">Battle queue that owns the deferred transition.</param>
    /// <returns><see langword="true"/> when the transition was queued.</returns>
    public static bool TryQueue(BattleSide attackerSide, int targetSlot, BattleFighter? target, IReadOnlyCollection<int> baseDigimonIds, int chance, IBattleRng rng, BattleTurnScheduler scheduler)
    {
        if (attackerSide != BattleSide.Enemy || targetSlot < 0 || target is not { IsKnockedOut: false } || !target.CanDedigivolve(baseDigimonIds))
        {
            return false;
        }

        if ((rng.NextValue() & 0x7F) >= chance)
        {
            return false;
        }

        scheduler.EnqueueDedigivolve(targetSlot);
        return true;
    }

    /// <summary>Consumes the next queued forced-return transition without touching unrelated pending events.</summary>
    /// <param name="state">Battle that owns the partner slot.</param>
    /// <param name="scheduler">Queue to inspect and consume.</param>
    /// <returns><see langword="true"/> when the next event was a forced-return transition.</returns>
    public static bool ProcessNext(BattleState state, BattleTurnScheduler scheduler)
    {
        if (scheduler.Count == 0 || scheduler.NextKind != BattleScheduledEventKind.Dedigivolve)
        {
            return false;
        }

        BattleScheduledEvent scheduled = scheduler.PopNext();
        BattleFighter? target = state.FighterAt(scheduled.Side, scheduled.FighterSlot);
        if (target is not null && target.CanDedigivolve(state.StarterDigimonIds))
        {
            target.Dedigivolve();
        }

        return true;
    }
}
