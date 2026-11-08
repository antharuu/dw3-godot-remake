namespace Dw3Production.Simulation;

/// <summary>Coordinates persistent poison and control statuses for one battle without owning its turn flow.</summary>
/// <param name="state">Battle exposing fighters, outcome checks and active slots.</param>
/// <param name="scheduler">Battle event queue containing status events.</param>
public sealed class BattleStatusCoordinator(BattleState state, BattleTurnScheduler scheduler)
{
    /// <summary>Attempts to poison the opposing active fighter using the qualified poison resistance roll.</summary>
    /// <param name="attackerSide">Side inflicting poison.</param>
    /// <param name="chance">Base application chance.</param>
    /// <param name="power">Stored periodic poison strength.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether poison was applied or refreshed.</returns>
    public bool InflictPoison(BattleSide attackerSide, int chance, int power, IBattleRng rng)
    {
        if (attackerSide == BattleSide.Player && state.Blocks[BattleConst.BlockPoison] != 0)
        {
            return false;
        }

        BattleFighter? attacker = state.FighterFor(attackerSide);
        BattleFighter? target = state.FighterFor(BattlePoisonRules.Opposite(attackerSide));
        if (attacker is not { IsKnockedOut: false } || target is not { IsKnockedOut: false })
        {
            return false;
        }

        attacker.RefreshStats();
        target.RefreshStats();
        if (!BattlePoisonRules.RollSucceeds(chance, attacker, target, rng))
        {
            return false;
        }

        BattleSide targetSide = BattlePoisonRules.Opposite(attackerSide);
        target.Flags |= FighterFlags.Poisoned;
        scheduler.EnqueuePoison(targetSide, state.ActiveSlot(targetSide), attacker.Special ? power * 2 : power);
        return true;
    }

    /// <summary>Cures poison from one fighter and removes its scheduled periodic damage.</summary>
    /// <param name="side">Side containing the cured fighter.</param>
    /// <param name="fighterSlot">Slot of the cured fighter.</param>
    public void CurePoison(BattleSide side, int fighterSlot)
    {
        BattleFighter? fighter = state.FighterAt(side, fighterSlot);
        if (fighter is not null)
        {
            fighter.Flags &= ~FighterFlags.Poisoned;
        }

        scheduler.RemovePoison(side, fighterSlot);
    }

    /// <summary>Attempts to apply one control status and refreshes exactly one expiry event on success.</summary>
    /// <param name="attackerSide">Side applying the status.</param>
    /// <param name="chance">Base application chance.</param>
    /// <param name="strength">Source status strength.</param>
    /// <param name="definition">Status-specific flag, event and resistance mapping.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether the status was applied.</returns>
    public bool InflictControl(BattleSide attackerSide, int chance, int strength, BattleControlStatusDefinition definition, IBattleRng rng)
    {
        BattleFighter? attacker = state.FighterFor(attackerSide);
        BattleFighter? target = state.FighterFor(BattlePoisonRules.Opposite(attackerSide));
        if (attacker is not { IsKnockedOut: false } || target is not { IsKnockedOut: false })
        {
            return false;
        }

        attacker.RefreshStats();
        target.RefreshStats();
        if (!BattleControlStatusRules.RollSucceeds(chance, attacker, target, definition.StatusResistance, definition.ElementResistance, rng))
        {
            return false;
        }

        BattleSide targetSide = BattlePoisonRules.Opposite(attackerSide);
        int appliedStrength = attacker.Special ? strength * 2 : strength;
        target.Flags |= definition.Flag;
        StoreStrength(target, definition.ExpiryKind, appliedStrength);
        int delay = BattleControlStatusRules.ExpiryDelay(definition.ExpiryKind, attacker, target, appliedStrength, rng);
        scheduler.EnqueueControlExpiry(definition.ExpiryKind, targetSide, state.ActiveSlot(targetSide), delay);
        return true;
    }

    /// <summary>Clears every control status and its expiry events from one fighter.</summary>
    /// <param name="side">Side containing the cured fighter.</param>
    /// <param name="fighterSlot">Slot of the cured fighter.</param>
    public void CureControlStatuses(BattleSide side, int fighterSlot)
    {
        BattleFighter? fighter = state.FighterAt(side, fighterSlot);
        if (fighter is not null)
        {
            fighter.Flags &= ~(FighterFlags.Paralyzed | FighterFlags.Confused | FighterFlags.Asleep);
            fighter.Paralysis = 0;
            fighter.Confusion = 0;
            fighter.Sleep = 0;
        }

        RemoveControlExpiries(side, fighterSlot);
    }

    /// <summary>Tests whether the active player loses its turn to paralysis.</summary>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether paralysis consumed the turn.</returns>
    public bool LosesPlayerTurnToParalysis(IBattleRng rng)
    {
        BattleFighter? fighter = state.ActivePlayerFighter;
        if (fighter is not { IsKnockedOut: false } || (fighter.Flags & FighterFlags.Paralyzed) == 0)
        {
            return false;
        }

        fighter.RefreshStats();
        return BattleControlStatusRules.LosesTurnToParalysis(fighter, rng);
    }

    /// <summary>Tests whether the active enemy loses its turn to paralysis.</summary>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether paralysis consumed the turn.</returns>
    public bool LosesEnemyTurnToParalysis(IBattleRng rng)
    {
        BattleFighter? fighter = state.ActiveEnemyFighter;
        if (fighter is not { IsKnockedOut: false } || (fighter.Flags & FighterFlags.Paralyzed) == 0)
        {
            return false;
        }

        fighter.RefreshStats();
        return BattleControlStatusRules.LosesTurnToParalysis(fighter, rng);
    }

    /// <summary>Tests whether a hit wakes the active fighter of one side.</summary>
    /// <param name="side">Side whose active fighter received the hit.</param>
    /// <param name="damage">Resolved damage value.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether sleep ended.</returns>
    public bool TryWakeOnHit(BattleSide side, int damage, IBattleRng rng)
    {
        BattleFighter? fighter = state.FighterFor(side);
        if (fighter is not { IsKnockedOut: false } || (fighter.Flags & FighterFlags.Asleep) == 0)
        {
            return false;
        }

        fighter.RefreshStats();
        if (!BattleControlStatusRules.WakesFromHit(fighter, damage, rng))
        {
            return false;
        }

        ClearSleep(side, state.ActiveSlot(side));
        return true;
    }

    /// <summary>Consumes all status events due before the next ordinary action.</summary>
    public void ProcessDueEvents()
    {
        while (scheduler.Count > 0 && IsStatusEvent(scheduler.NextKind))
        {
            ProcessEvent(scheduler.PopNext());
        }
    }

    /// <summary>Stores one control-status strength in its matching fighter field.</summary>
    /// <param name="fighter">Fighter receiving the status.</param>
    /// <param name="kind">Expiry event identifying the status.</param>
    /// <param name="strength">Strength to store.</param>
    private static void StoreStrength(BattleFighter fighter, BattleScheduledEventKind kind, int strength)
    {
        switch (kind)
        {
            case BattleScheduledEventKind.ParalysisEnd:
                fighter.Paralysis = strength;
                break;
            case BattleScheduledEventKind.ConfusionEnd:
                fighter.Confusion = strength;
                break;
            case BattleScheduledEventKind.SleepEnd:
                fighter.Sleep = strength;
                break;
        }
    }

    /// <summary>Gets whether a queued event changes a persistent status.</summary>
    /// <param name="kind">Scheduled event kind.</param>
    /// <returns>Whether the event is a status event.</returns>
    private static bool IsStatusEvent(BattleScheduledEventKind kind) =>
        kind is BattleScheduledEventKind.PoisonDamage or BattleScheduledEventKind.ParalysisEnd or BattleScheduledEventKind.ConfusionEnd or BattleScheduledEventKind.SleepEnd;

    /// <summary>Processes one persistent-status event.</summary>
    /// <param name="scheduled">Due event.</param>
    private void ProcessEvent(BattleScheduledEvent scheduled)
    {
        if (scheduled.Kind == BattleScheduledEventKind.PoisonDamage)
        {
            ProcessPoisonDamage(scheduled);
            return;
        }

        ClearExpiredControl(scheduled);
    }

    /// <summary>Resolves one poison tick and re-arms it while its fighter can still receive ticks.</summary>
    /// <param name="scheduled">Due poison event.</param>
    private void ProcessPoisonDamage(BattleScheduledEvent scheduled)
    {
        BattleFighter? fighter = state.FighterAt(scheduled.Side, scheduled.FighterSlot);
        if (fighter is null || (fighter.Flags & FighterFlags.Poisoned) == 0 || fighter.IsKnockedOut)
        {
            return;
        }

        if (state.ActiveSlot(scheduled.Side) == scheduled.FighterSlot)
        {
            fighter.RefreshStats();
            fighter.TakeDamage(BattlePoisonRules.Damage(scheduled.Value, fighter));
            state.CheckOutcome();
        }

        if (fighter.IsKnockedOut)
        {
            fighter.Flags &= ~FighterFlags.Poisoned;
            return;
        }

        scheduler.EnqueuePoison(scheduled.Side, scheduled.FighterSlot, scheduled.Value);
    }

    /// <summary>Clears the control status matching a due expiry event.</summary>
    /// <param name="scheduled">Due expiry event.</param>
    private void ClearExpiredControl(BattleScheduledEvent scheduled)
    {
        BattleFighter? fighter = state.FighterAt(scheduled.Side, scheduled.FighterSlot);
        if (fighter is null)
        {
            return;
        }

        switch (scheduled.Kind)
        {
            case BattleScheduledEventKind.ParalysisEnd:
                fighter.Flags &= ~FighterFlags.Paralyzed;
                fighter.Paralysis = 0;
                break;
            case BattleScheduledEventKind.ConfusionEnd:
                fighter.Flags &= ~FighterFlags.Confused;
                fighter.Confusion = 0;
                break;
            case BattleScheduledEventKind.SleepEnd:
                fighter.Flags &= ~FighterFlags.Asleep;
                fighter.Sleep = 0;
                break;
        }
    }

    /// <summary>Clears sleep and removes its expiry event.</summary>
    /// <param name="side">Side containing the fighter.</param>
    /// <param name="fighterSlot">Slot of the fighter to wake.</param>
    private void ClearSleep(BattleSide side, int fighterSlot)
    {
        BattleFighter? fighter = state.FighterAt(side, fighterSlot);
        if (fighter is not null)
        {
            fighter.Flags &= ~FighterFlags.Asleep;
            fighter.Sleep = 0;
        }

        scheduler.RemoveControlExpiry(BattleScheduledEventKind.SleepEnd, side, fighterSlot);
    }

    /// <summary>Removes all control expiry events for one fighter.</summary>
    /// <param name="side">Side containing the fighter.</param>
    /// <param name="fighterSlot">Slot of the fighter being cured.</param>
    private void RemoveControlExpiries(BattleSide side, int fighterSlot)
    {
        scheduler.RemoveControlExpiry(BattleScheduledEventKind.ParalysisEnd, side, fighterSlot);
        scheduler.RemoveControlExpiry(BattleScheduledEventKind.ConfusionEnd, side, fighterSlot);
        scheduler.RemoveControlExpiry(BattleScheduledEventKind.SleepEnd, side, fighterSlot);
    }
}
