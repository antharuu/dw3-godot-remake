namespace Dw3Production.Simulation;

/// <summary>Owns temporary stat boosts, charge values, and field-element boosts for one battle.</summary>
/// <param name="state">Battle providing mutable fighters and active form snapshots.</param>
/// <param name="scheduler">Battle event queue that owns modifier expiries.</param>
public sealed class BattleModifierCoordinator(BattleState state, BattleTurnScheduler scheduler)
{
    /// <summary>Applies one percentage stat delta and refreshes exactly one matching expiry.</summary>
    /// <param name="side">Side containing the fighter.</param>
    /// <param name="fighterSlot">Fighter slot receiving the modifier.</param>
    /// <param name="stat">Attack, defense, or speed stat to modify.</param>
    /// <param name="percent">Delta in 128ths of the stat without its existing modifier.</param>
    /// <param name="fromTechnique">Whether the producer uses the longer technique expiry.</param>
    /// <param name="rng">Shared battle random source used for expiry timing.</param>
    /// <returns>Whether a living fighter accepted the modifier.</returns>
    public bool ApplyStatModifier(BattleSide side, int fighterSlot, int stat, int percent, bool fromTechnique, IBattleRng rng)
    {
        BattleFighter? fighter = state.FighterAt(side, fighterSlot);
        int boostIndex = BoostIndex(stat);
        if (fighter is not { Id: not 0, IsKnockedOut: false } || boostIndex < 0)
        {
            return false;
        }

        fighter.RefreshStats();
        int baseValue = Math.Max(fighter.Stats.Stats[stat] - fighter.Boosts[boostIndex], 1);
        int changed = fighter.Boosts[boostIndex] + ((baseValue * percent) / 128);
        fighter.Boosts[boostIndex] = Math.Clamp(changed, -(baseValue / 2), baseValue);
        fighter.RefreshStats();
        scheduler.EnqueueControlExpiry(BoostEndKind(boostIndex), side, fighterSlot, ExpiryDelay(fighter, fromTechnique, rng));
        return true;
    }

    /// <summary>Sets a fighter's next-technique charge and refreshes its resolved damage bonus.</summary>
    /// <param name="side">Side containing the fighter.</param>
    /// <param name="value">Charge in sixty-fourths of damage.</param>
    /// <returns>Whether a living fighter accepted the charge.</returns>
    public bool SetCharge(BattleSide side, int value)
    {
        BattleFighter? fighter = state.FighterFor(side);
        if (fighter is not { IsKnockedOut: false })
        {
            return false;
        }

        fighter.Charge = value;
        fighter.RefreshStats();
        return true;
    }

    /// <summary>Applies one temporary elemental field boost and retains the replaced field for expiry.</summary>
    /// <param name="boost">Valid elemental field value to expose while this effect is active.</param>
    /// <param name="rng">Shared battle random source used for expiry timing.</param>
    /// <returns>Whether the boost has a supported element and was scheduled.</returns>
    public bool ApplyElementBoost(ElementBoost boost, IBattleRng rng)
    {
        if (!HasSupportedElement(boost))
        {
            return false;
        }

        ElementBoost previous = state.Boost;
        state.Boost = boost;
        scheduler.EnqueueElementBoostExpiry(previous, FieldExpiryDelay(rng));
        return true;
    }

    /// <summary>Applies a temporary special state to one living fighter.</summary>
    /// <param name="side">Side containing the fighter.</param>
    /// <param name="fighterSlot">Slot receiving the state.</param>
    /// <param name="rng">Shared battle random source used for expiry timing.</param>
    /// <returns>Whether a living fighter received the state.</returns>
    public bool ApplySpecialStatus(BattleSide side, int fighterSlot, IBattleRng rng)
    {
        BattleFighter? fighter = state.FighterAt(side, fighterSlot);
        if (fighter is not { Id: not 0, IsKnockedOut: false })
        {
            return false;
        }

        fighter.Special = true;
        scheduler.EnqueueControlExpiry(BattleScheduledEventKind.SpecialEnd, side, fighterSlot, ExpiryDelay(fighter, fromTechnique: false, rng));
        return true;
    }

    /// <summary>Consumes a fighter's pending charge after its next technique begins.</summary>
    /// <param name="side">Side containing the fighter.</param>
    public void ConsumeCharge(BattleSide side)
    {
        BattleFighter? fighter = state.FighterFor(side);
        if (fighter is not null && fighter.Charge != 0)
        {
            fighter.Charge = 0;
            fighter.RefreshStats();
        }
    }

    /// <summary>Processes one due modifier expiry, if the next queued event is a modifier event.</summary>
    /// <returns>Whether an expiry was consumed.</returns>
    public bool ProcessNextExpiry()
    {
        if (scheduler.Count == 0 || !IsModifierEvent(scheduler.NextKind))
        {
            return false;
        }

        ClearBoost(scheduler.PopNext());
        return true;
    }

    /// <summary>Maps exposed stats to the fighter's temporary boost slots.</summary>
    /// <param name="stat">Exposed stat index.</param>
    /// <returns>Boost slot, or -1 for an unsupported stat.</returns>
    private static int BoostIndex(int stat) => stat switch
    {
        BattleStat.Attack => 0,
        BattleStat.Defense => 1,
        BattleStat.Speed => 2,
        _ => -1,
    };

    /// <summary>Maps a boost slot to its scheduled expiry kind.</summary>
    /// <param name="boostIndex">Fighter boost slot.</param>
    /// <returns>Matching expiry event kind.</returns>
    private static BattleScheduledEventKind BoostEndKind(int boostIndex) => boostIndex switch
    {
        0 => BattleScheduledEventKind.AttackBoostEnd,
        1 => BattleScheduledEventKind.DefenseBoostEnd,
        2 => BattleScheduledEventKind.SpeedBoostEnd,
        _ => throw new ArgumentOutOfRangeException(nameof(boostIndex)),
    };

    /// <summary>Gets the source-specific expiry delay from the current spirit snapshot.</summary>
    /// <param name="fighter">Fighter whose spirit selects the delay.</param>
    /// <param name="fromTechnique">Whether the longer technique multiplier applies.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Expiry delay in battle-time units.</returns>
    private static int ExpiryDelay(BattleFighter fighter, bool fromTechnique, IBattleRng rng)
    {
        int multiplier = fromTechnique ? 12 : 8;
        return (fighter.Stats.Stats[BattleStat.Spirit] * multiplier) + 1000 + (rng.NextValue() % 2001);
    }

    /// <summary>Tests whether a field effect names one of the seven defined battle elements.</summary>
    /// <param name="boost">Candidate temporary field value.</param>
    /// <returns>Whether its element is valid for damage resolution.</returns>
    private static bool HasSupportedElement(ElementBoost boost) =>
        boost.Element >= BattleConst.ElementFirst && boost.Element < BattleConst.ElementFirst + BattleConst.ElementCount;

    /// <summary>Gets the field-effect expiry delay without coupling the field to a fighter snapshot.</summary>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Expiry delay in battle-time units.</returns>
    private static int FieldExpiryDelay(IBattleRng rng) =>
        BattleActionDelay.Calculate(BattleActionDelayKind.BoostExpiry, 0, 0, 0, rng);

    /// <summary>Gets whether an event ends one temporary battle modifier.</summary>
    /// <param name="kind">Queued event kind.</param>
    /// <returns>Whether the kind belongs to this coordinator.</returns>
    private static bool IsModifierEvent(BattleScheduledEventKind kind) => kind is BattleScheduledEventKind.AttackBoostEnd or BattleScheduledEventKind.DefenseBoostEnd or BattleScheduledEventKind.SpeedBoostEnd or BattleScheduledEventKind.ElementBoostEnd or BattleScheduledEventKind.SpecialEnd;

    /// <summary>Clears the one modifier addressed by an expiry event.</summary>
    /// <param name="scheduled">Consumed modifier expiry.</param>
    private void ClearBoost(BattleScheduledEvent scheduled)
    {
        if (scheduled.Kind == BattleScheduledEventKind.ElementBoostEnd)
        {
            state.Boost = scheduled.PreviousBoost;
            return;
        }

        BattleFighter? fighter = state.FighterAt(scheduled.Side, scheduled.FighterSlot);
        if (scheduled.Kind == BattleScheduledEventKind.SpecialEnd)
        {
            if (fighter is { } special)
            {
                special.Special = false;
            }

            return;
        }

        int index = scheduled.Kind switch
        {
            BattleScheduledEventKind.AttackBoostEnd => 0,
            BattleScheduledEventKind.DefenseBoostEnd => 1,
            BattleScheduledEventKind.SpeedBoostEnd => 2,
            _ => -1,
        };
        if (fighter is not null && index >= 0)
        {
            fighter.Boosts[index] = 0;
            fighter.RefreshStats();
        }
    }
}
