namespace Dw3Production.Simulation;

/// <summary>Schedules and consumes the persistent automatic recovery granted by item-tech 0xBD.</summary>
/// <param name="state">Battle state containing the recovery target and observable pending marker.</param>
/// <param name="scheduler">Relative-time queue shared with turns and other battle events.</param>
public sealed class BattleAutoRecoveryCoordinator(BattleState state, BattleTurnScheduler scheduler)
{
    /// <summary>Arms a stable target's recurring recovery and refreshes its expiry without duplicating either event.</summary>
    /// <param name="recovery">Owner, stable target slot and source technique identity.</param>
    public void Arm(BattleAutoRecovery recovery)
    {
        BattleFighter? fighter = state.FighterAt(recovery.Side, recovery.FighterSlot);
        if (fighter is not { IsKnockedOut: false })
        {
            return;
        }

        fighter.RefreshStats();
        scheduler.EnqueueAutoRecovery(recovery.Side, recovery.FighterSlot);
        scheduler.EnqueueAutoRecoveryEnd(recovery.Side, recovery.FighterSlot, ExpiryDelay(fighter));
        state.PendingAutoRecovery = recovery;
    }

    /// <summary>Consumes all queued automatic recovery events that are due before an ordinary action.</summary>
    /// <returns>Whether at least one recovery or expiry event ran.</returns>
    public bool ProcessDueEvents()
    {
        bool any = false;
        while (scheduler.Count > 0 && IsRecoveryEvent(scheduler.NextKind))
        {
            ProcessEvent(scheduler.PopNext());
            any = true;
        }

        return any;
    }

    /// <summary>Calculates the source auto-recovery expiry delay from active spirit and one 0..1000 draw.</summary>
    /// <param name="fighter">Fighter whose resolved spirit qualifies the expiry.</param>
    /// <returns>Delay in battle-time units.</returns>
    private static int ExpiryDelay(BattleFighter fighter) =>
        (new BattleDefaultOpeningRng().NextValue() % 1001) + (Math.Max(BattleFighterTiming.SpiritOf(fighter), 0) * 10);

    /// <summary>Tests whether a scheduled event belongs to this coordinator.</summary>
    /// <param name="kind">Scheduled event kind.</param>
    /// <returns>Whether the event is an item auto-recovery tick or expiry.</returns>
    private static bool IsRecoveryEvent(BattleScheduledEventKind kind) =>
        kind is BattleScheduledEventKind.AutoRecovery or BattleScheduledEventKind.AutoRecoveryEnd;

    /// <summary>Maps the source recovery technique identity to the random big-recovery formula.</summary>
    /// <param name="fighter">Fighter whose maximum HP defines the source percentage.</param>
    /// <param name="techniqueId">Technique identity stored in the recovery event.</param>
    /// <param name="rng">Default scheduler source used when no runtime action stream is available.</param>
    /// <returns>The qualified recovery amount.</returns>
    private static int RecoveryAmount(BattleFighter fighter, int techniqueId, BattleDefaultOpeningRng rng)
    {
        int range = techniqueId == 0xBD ? 9 : 5;
        int basePercent = techniqueId == 0xBD ? 8 : 4;
        return Math.Min((fighter.MaxHp * ((rng.NextValue() % range) + basePercent)) / 128, 9999);
    }

    /// <summary>Dispatches one due recovery event.</summary>
    /// <param name="scheduled">Event selected by the shared scheduler.</param>
    private void ProcessEvent(BattleScheduledEvent scheduled)
    {
        if (scheduled.Kind == BattleScheduledEventKind.AutoRecovery)
        {
            Recover(scheduled);
            return;
        }

        RestoreEquipmentRecoveryOrRemove(scheduled);
    }

    /// <summary>Applies one source tick only while the original target remains living, then rearms the normal cadence.</summary>
    /// <param name="scheduled">Stable target selected at activation.</param>
    private void Recover(BattleScheduledEvent scheduled)
    {
        BattleFighter? fighter = state.FighterAt(scheduled.Side, scheduled.FighterSlot);
        if (fighter is not { IsKnockedOut: false })
        {
            ClearPendingRecovery(scheduled.Side, scheduled.FighterSlot);
            return;
        }

        if (fighter.Hp < fighter.MaxHp)
        {
            fighter.Hp = Math.Min(fighter.Hp + RecoveryAmount(fighter, scheduled.Value, new BattleDefaultOpeningRng()), fighter.MaxHp);
        }

        if (scheduled.Value == 0 && fighter.HasRecoveryEquipment)
        {
            scheduler.EnqueueEquipmentRecovery(scheduled.Side, scheduled.FighterSlot);
            return;
        }

        if (HasPendingRecovery(scheduled.Side, scheduled.FighterSlot))
        {
            scheduler.EnqueueAutoRecovery(scheduled.Side, scheduled.FighterSlot);
        }
    }

    /// <summary>Restores baseline equipment recovery after item expiry or removes a recovery owned only by item-tech 0xBD.</summary>
    /// <param name="scheduled">Expired item recovery event with its stable identity.</param>
    private void RestoreEquipmentRecoveryOrRemove(BattleScheduledEvent scheduled)
    {
        BattleFighter? fighter = state.FighterAt(scheduled.Side, scheduled.FighterSlot);
        if (fighter?.HasRecoveryEquipment == true)
        {
            scheduler.EnqueueEquipmentRecovery(scheduled.Side, scheduled.FighterSlot);
        }
        else
        {
            scheduler.RemoveAutoRecovery(scheduled.Side, scheduled.FighterSlot);
        }

        ClearPendingRecovery(scheduled.Side, scheduled.FighterSlot);
    }

    /// <summary>Tests whether this coordinator still owns the stable recovery target.</summary>
    /// <param name="side">Recovery owner side.</param>
    /// <param name="fighterSlot">Stable recovery target slot.</param>
    /// <returns>Whether the matching pending marker is active.</returns>
    private bool HasPendingRecovery(BattleSide side, int fighterSlot) =>
        state.PendingAutoRecovery is { } recovery && recovery.Side == side && recovery.FighterSlot == fighterSlot;

    /// <summary>Clears a pending marker only when the consumed event belongs to its stable target.</summary>
    /// <param name="side">Recovery owner side.</param>
    /// <param name="fighterSlot">Stable recovery target slot.</param>
    private void ClearPendingRecovery(BattleSide side, int fighterSlot)
    {
        if (HasPendingRecovery(side, fighterSlot))
        {
            state.PendingAutoRecovery = null;
        }
    }
}
