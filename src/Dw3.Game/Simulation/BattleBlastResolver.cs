namespace Dw3Production.Simulation;

/// <summary>Triggers the blast digivolution and resolves the scheduled return to the pre-blast form.</summary>
/// <remarks>
/// The blast is automatic: when the gauge reaches <see cref="BattleBlastGaugeRules.GaugeThreshold"/>
/// during a player action that deals damage, the active partner changes into a temporary blast form
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
        fighter.BlastGauge = 0;
        fighter.RefreshStats();
        scheduler.EnqueueBlastReturn(fighterSlot, returnDelay);
        return true;
    }

    /// <summary>Charges the gauge from a player action's total damage and triggers the blast when the threshold is reached.</summary>
    /// <param name="state">Battle owning the gauge callback and blast-form catalogue.</param>
    /// <param name="activeSlot">Active player party slot.</param>
    /// <param name="attacker">Active player fighter whose gauge is ticked, or <see langword="null"/> to skip.</param>
    /// <param name="totalDamage">Total HP removed from the enemy by the action (never negative).</param>
    /// <param name="scheduler">Battle event queue for the return event.</param>
    public static void TryTickAndTrigger(BattleState state, int activeSlot, BattleFighter? attacker, int totalDamage, BattleTurnScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scheduler);
        if (attacker is null || attacker.Temporary || totalDamage <= 0)
        {
            return;
        }

        int previous = attacker.BlastGauge;
        int raw = previous + BattleBlastGaugeRules.ComputeGain(totalDamage);
        attacker.BlastGauge = BattleBlastGaugeRules.ClampGauge(raw);
        if (!BattleBlastGaugeRules.IsTriggered(previous, attacker.BlastGauge))
        {
            return;
        }

        TryTriggerFromState(state, activeSlot, attacker, scheduler);
    }

    /// <summary>Processes the next blast-return event and reverts the partner to its pre-blast form.</summary>
    /// <param name="state">Battle owning the partner slot.</param>
    /// <param name="scheduler">Queue to inspect and consume.</param>
    /// <returns><see langword="true"/> when the next queued event was a blast return and was consumed.</returns>
    public static bool ProcessNext(BattleState state, BattleTurnScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scheduler);
        if (scheduler.Count == 0 || scheduler.NextKind != BattleScheduledEventKind.BlastReturn)
        {
            return false;
        }

        BattleScheduledEvent scheduled = scheduler.PopNext();
        BattleFighter? fighter = state.FighterAt(scheduled.Side, scheduled.FighterSlot);
        if (fighter is not null && fighter.Temporary)
        {
            ApplyReturn(fighter);
            state.NotifyActiveFormChanged();
        }

        return true;
    }

    /// <summary>Reverts a blast fighter to its pre-blast form and clears the temporary flag.</summary>
    /// <param name="fighter">Fighter currently in a blast form.</param>
    private static void ApplyReturn(BattleFighter fighter)
    {
        fighter.Id = fighter.PrevId;
        fighter.Temporary = false;
        fighter.RefreshStats();
    }

    /// <summary>Looks up the blast form from the state's catalogue and applies the blast transformation.</summary>
    /// <param name="state">Battle supplying the blast-form catalogue and callback.</param>
    /// <param name="activeSlot">Active player slot.</param>
    /// <param name="attacker">Fighter whose gauge threshold was reached.</param>
    /// <param name="scheduler">Battle event queue for the return event.</param>
    private static void TryTriggerFromState(BattleState state, int activeSlot, BattleFighter attacker, BattleTurnScheduler scheduler)
    {
        if (!state.BlastFormsById.TryGetValue(attacker.Id, out int blastForm) || blastForm == 0)
        {
            return;
        }

        int tier = BattleBlastGaugeRules.SelectTier(state.BlastTierLevels, attacker.Stats.Level);
        int delay = BattleActionDelay.Calculate(BattleBlastGaugeRules.DelayKindForTier(tier), 0, 0, 0, new BattleDefaultOpeningRng());
        if (TryTrigger(attacker, activeSlot, blastForm, delay, scheduler))
        {
            state.NotifyActiveFormChanged();
        }
    }
}
