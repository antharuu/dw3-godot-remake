namespace Dw3Production.Simulation;

/// <summary>Triggers the blast digivolution and resolves the scheduled return to the pre-blast form.</summary>
/// <remarks>
/// The blast is automatic: when the gauge reaches <see cref="BattleBlastGaugeRules.GaugeThreshold"/>
/// after a qualifying enemy action damages a surviving partner, the active partner changes into a temporary blast form
/// and a timed return event is scheduled. The form change is not a command and cannot be blocked
/// by <see cref="FighterFlags.NoDigivolve"/>. The partner's pre-blast form is preserved in
/// <see cref="BattleFighter.PrevId"/> so the return is exact even when the partner was already in
/// a non-origin form at blast time.
/// </remarks>
public static class BattleBlastResolver
{
    /// <summary>Applies the blast form transformation to the active partner and schedules its return.</summary>
    /// <param name="fighter">Active player fighter entering the blast form.</param>
    /// <param name="fighterSlot">Party slot index of the active partner.</param>
    /// <param name="blastFormId">Target blast form Digimon id, non-zero.</param>
    /// <param name="returnDelay">Scheduled return delay in battle-time units.</param>
    /// <param name="scheduler">Battle event queue receiving the return event.</param>
    /// <returns><see langword="true"/> when the blast was applied; <see langword="false"/> when the fighter cannot accept it.</returns>
    public static bool TryTrigger(BattleFighter fighter, int fighterSlot, int blastFormId, int returnDelay, BattleTurnScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (fighter is null || fighter.IsKnockedOut || blastFormId == 0 || fighter.Temporary)
        {
            return false;
        }

        fighter.PrevId = fighter.Id;
        fighter.Id = blastFormId;
        fighter.Temporary = true;
        fighter.Hp = fighter.MaxHp;
        fighter.Flags &= ~FighterFlags.Confused;
        fighter.Confusion = 0;
        scheduler.RemoveControlExpiry(BattleScheduledEventKind.ConfusionEnd, BattleSide.Player, fighterSlot);
        fighter.RefreshStats();
        scheduler.EnqueueBlastReturn(fighterSlot, returnDelay);
        return true;
    }

    /// <summary>Charges the gauge from a completed enemy action's qualified damage and triggers the blast when the threshold is reached.</summary>
    /// <param name="state">Battle owning the gauge callback and blast-form catalogue.</param>
    /// <param name="activeSlot">Active player party slot.</param>
    /// <param name="attacker">Active player fighter whose gauge is ticked, or <see langword="null"/> to skip.</param>
    /// <param name="totalDamage">Qualified damage reported by the enemy action.</param>
    /// <param name="scheduler">Battle event queue for the return event.</param>
    /// <param name="rng">Shared random stream used by partner-specific return delays.</param>
    public static void TryTickAndTrigger(BattleState state, int activeSlot, BattleFighter? attacker, int totalDamage, BattleTurnScheduler scheduler, IBattleRng rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scheduler);
        if (attacker is null || attacker.IsKnockedOut || attacker.Temporary || totalDamage <= 0)
        {
            return;
        }

        int previous = attacker.BlastGauge;
        int raw = previous + BattleBlastGaugeRules.ComputeGain(totalDamage, attacker.MaxHp, attacker.Blast?.Accessories);
        attacker.BlastGauge = BattleBlastGaugeRules.ClampGauge(raw);
        if (!BattleBlastGaugeRules.IsTriggered(previous, attacker.BlastGauge))
        {
            return;
        }

        if (attacker.Blast is not null)
        {
            attacker.PendingBlastRng = rng;
            scheduler.EnqueueControlExpiry(BattleScheduledEventKind.BlastStart, BattleSide.Player, activeSlot, 0);
        }
    }

    /// <summary>Processes the next blast-return event and reverts the partner to its pre-blast form.</summary>
    /// <param name="state">Battle owning the partner slot.</param>
    /// <param name="scheduler">Queue to inspect and consume.</param>
    /// <returns><see langword="true"/> when the next queued event was a blast return and was consumed.</returns>
    public static bool ProcessNext(BattleState state, BattleTurnScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scheduler);
        if (scheduler.Count == 0 || scheduler.NextKind is not (BattleScheduledEventKind.BlastStart or BattleScheduledEventKind.BlastReturn))
        {
            return false;
        }

        BattleScheduledEvent scheduled = scheduler.PopNext();
        BattleFighter? fighter = state.FighterAt(scheduled.Side, scheduled.FighterSlot);
        if (fighter is not null && scheduled.Kind == BattleScheduledEventKind.BlastStart)
        {
            IBattleRng rng = fighter.PendingBlastRng ?? throw new InvalidOperationException("Queued Blast has no action stream.");
            fighter.PendingBlastRng = null;
            TryTriggerFromState(state, scheduled.FighterSlot, fighter, scheduler, rng);
        }
        else if (fighter is { Temporary: true })
        {
            ApplyReturn(fighter);
            state.NotifyActiveFormChanged();
        }

        return true;
    }

    /// <summary>Restores a temporary form without healing or resetting a normal partner's gauge.</summary>
    /// <param name="fighter">Partner being returned or committed.</param>
    public static void EndTemporaryForm(BattleFighter fighter)
    {
        if (fighter.Temporary)
        {
            ApplyReturn(fighter);
        }
    }

    /// <summary>Removes the temporary state and its queued return from one stable party slot.</summary>
    /// <param name="state">Battle owning the slot.</param>
    /// <param name="slot">Party slot, independent of the active index.</param>
    /// <param name="scheduler">Battle event queue receiving cancellation.</param>
    public static void Cancel(BattleState state, int slot, BattleTurnScheduler scheduler)
    {
        BattleFighter? fighter = state.FighterAt(BattleSide.Player, slot);
        if (fighter is not null)
        {
            EndTemporaryForm(fighter);
            fighter.PendingBlastRng = null;
        }

        scheduler.RemoveControlExpiry(BattleScheduledEventKind.BlastStart, BattleSide.Player, slot);
        scheduler.RemoveControlExpiry(BattleScheduledEventKind.BlastReturn, BattleSide.Player, slot);
    }

    /// <summary>Cancels every temporary partner at exit, or only knocked-out partners while combat continues.</summary>
    /// <param name="state">Battle owning the party slots.</param>
    /// <param name="scheduler">Queue from which cancelled returns are removed.</param>
    /// <param name="all">Whether this is an exit rather than an in-battle KO sweep.</param>
    public static void CancelInactive(BattleState state, BattleTurnScheduler scheduler, bool all)
    {
        for (int slot = 0; slot < state.Players.Length; slot++)
        {
            if (all || state.Players[slot] is { IsKnockedOut: true })
            {
                Cancel(state, slot, scheduler);
            }
        }
    }

    /// <summary>Reverts a blast fighter to its pre-blast form and clears the temporary flag.</summary>
    /// <param name="fighter">Fighter currently in a blast form.</param>
    private static void ApplyReturn(BattleFighter fighter)
    {
        fighter.Id = fighter.PrevId;
        fighter.Temporary = false;
        fighter.BlastGauge = 0;
        fighter.RefreshStats();
    }

    /// <summary>Looks up the blast form from the state's catalogue and applies the blast transformation.</summary>
    /// <param name="state">Battle supplying the blast-form catalogue and callback.</param>
    /// <param name="activeSlot">Active player slot.</param>
    /// <param name="attacker">Fighter whose gauge threshold was reached.</param>
    /// <param name="scheduler">Battle event queue for the return event.</param>
    /// <param name="rng">Shared random stream used by partner-specific return delays.</param>
    private static void TryTriggerFromState(BattleState state, int activeSlot, BattleFighter attacker, BattleTurnScheduler scheduler, IBattleRng rng)
    {
        if (attacker.Blast is not { FormId: > 0 } configuration)
        {
            return;
        }

        if (TryTrigger(attacker, activeSlot, configuration.FormId, 0, scheduler))
        {
            int delay = ReturnDelay(state, attacker, configuration.PartnerIndex, rng);
            scheduler.EnqueueBlastReturn(activeSlot, delay);
            state.NotifyActiveFormChanged();
        }
    }

    /// <summary>Evaluates the partner-selected delay with current transformed stats and the shared battle stream.</summary>
    /// <param name="state">Battle supplying opposing resistances.</param>
    /// <param name="fighter">Transformed partner.</param>
    /// <param name="partnerIndex">Permanent partner definition ordinal.</param>
    /// <param name="rng">Shared stream, consumed only by random delay families.</param>
    /// <returns>Delay in scheduler units.</returns>
    private static int ReturnDelay(BattleState state, BattleFighter fighter, int partnerIndex, IBattleRng rng)
    {
        BattleActionDelayKind kind = BattleBlastGaugeRules.DelayKindForPartner(partnerIndex);
        if (kind is BattleActionDelayKind.Paralysis or BattleActionDelayKind.Confusion)
        {
            BattleScheduledEventKind expiry = kind == BattleActionDelayKind.Paralysis
                ? BattleScheduledEventKind.ParalysisEnd : BattleScheduledEventKind.ConfusionEnd;
            return BattleControlStatusRules.ExpiryDelay(expiry, fighter, state.ActiveEnemyFighter ?? throw new InvalidOperationException("Blast requires an opposing fighter."), state.LastControlStrength, rng);
        }

        return BattleActionDelay.Calculate(kind, 0, 0, 0, rng);
    }
}
