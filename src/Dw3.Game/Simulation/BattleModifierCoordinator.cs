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

    /// <summary>Applies one exact source-unit delta to a temporary stat modifier and refreshes its expiry.</summary>
    /// <param name="side">Side containing the fighter.</param>
    /// <param name="fighterSlot">Fighter slot receiving the modifier.</param>
    /// <param name="stat">Target attack or defense stat.</param>
    /// <param name="delta">Source-unit delta applied after resolving the current stat.</param>
    /// <param name="rng">Shared battle random source used for expiry timing.</param>
    /// <returns>Whether the modifier changed and an expiry was queued.</returns>
    public bool ApplyStatDelta(BattleSide side, int fighterSlot, int stat, int delta, IBattleRng rng) =>
        BattleExactStatModifier.Apply(state, scheduler, side, fighterSlot, stat, delta, rng);

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

    /// <summary>Applies an enemy technique's temporary lock to the active player after its chance succeeds.</summary>
    /// <param name="effect">Restriction effect identifier.</param>
    /// <param name="chance">Extracted chance, out of 128.</param>
    /// <param name="power">Extracted duration multiplier.</param>
    /// <param name="rng">Shared battle random source for the chance and successful duration.</param>
    /// <returns>Whether a living active player accepted a successful supported restriction.</returns>
    public bool ApplyRestriction(int effect, int chance, int power, IBattleRng rng)
    {
        if (!TryRestriction(effect, out int flag, out BattleScheduledEventKind expiry))
        {
            return false;
        }

        BattleFighter? target = state.ActivePlayerFighter;
        if (target is not { IsKnockedOut: false } || (rng.NextValue() & 0x7F) >= chance)
        {
            return false;
        }

        target.Flags |= flag;
        scheduler.EnqueueControlExpiry(expiry, BattleSide.Player, state.ActivePlayer, RestrictionDelay(power, rng));
        return true;
    }

    /// <summary>Applies a landed attack or defense decrease to the opposing active fighter.</summary>
    /// <param name="attackerSide">Side that landed the lowering technique.</param>
    /// <param name="effect">Extracted lowering effect identifier.</param>
    /// <param name="power">Extracted decrease in 128ths.</param>
    /// <param name="rng">Shared battle random source used only by a successful expiry.</param>
    /// <returns>Whether the supported decrease changed an eligible target.</returns>
    public bool ApplyStatLowering(BattleSide attackerSide, int effect, int power, IBattleRng rng)
    {
        if (!TryLoweredStat(effect, out int stat) || (attackerSide == BattleSide.Player && state.Blocks[stat == BattleStat.Attack ? BattleConst.BlockLowerAttack : BattleConst.BlockLowerDefense] != 0))
        {
            return false;
        }

        BattleSide targetSide = BattlePoisonRules.Opposite(attackerSide);
        return ApplyStatModifier(targetSide, state.ActiveSlot(targetSide), stat, -power, fromTechnique: true, rng);
    }

    /// <summary>Raises persistent statuses on the active player by the exact status-effect contract.</summary>
    /// <param name="effect">Extracted status-raise effect identifier.</param>
    /// <param name="chance">Extracted base chance.</param>
    /// <param name="power">Extracted additive status amount.</param>
    /// <param name="rng">Shared battle random source for selection and status rolls.</param>
    /// <returns>Whether at least one persistent status was raised.</returns>
    public bool ApplyStatusRaise(int effect, int chance, int power, IBattleRng rng)
    {
        BattleFighter? target = state.ActivePlayerFighter;
        BattleFighter? enemy = state.ActiveEnemyFighter;
        if (target is not { IsKnockedOut: false } || enemy is null)
        {
            return false;
        }

        return effect switch
        {
            BattleTechniqueEffect.RaiseOneStatus => RaiseOne(target, chance, power, enemy, rng),
            BattleTechniqueEffect.RaiseEachStatus => RaiseEach(target, chance, power, enemy, rng),
            BattleTechniqueEffect.RaiseAllStatus => RaiseAll(target, chance, power, enemy, rng),
            _ => false,
        };
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
        if (BattleDedigivolveResolver.ProcessNext(state, scheduler))
        {
            return true;
        }

        if (BattleBlastResolver.ProcessNext(state, scheduler))
        {
            return true;
        }

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
    private static bool IsModifierEvent(BattleScheduledEventKind kind) => kind is BattleScheduledEventKind.AttackBoostEnd or BattleScheduledEventKind.DefenseBoostEnd or BattleScheduledEventKind.SpeedBoostEnd or BattleScheduledEventKind.ElementBoostEnd or BattleScheduledEventKind.SpecialEnd or BattleScheduledEventKind.NoSwitchEnd or BattleScheduledEventKind.NoDigivolveEnd;

    /// <summary>Maps a supported restriction effect to its fighter flag and expiry event.</summary>
    /// <param name="effect">Extracted technique effect.</param>
    /// <param name="flag">Flag applied while the restriction is active.</param>
    /// <param name="expiry">Scheduled event that ends the restriction.</param>
    /// <returns>Whether the effect is a supported restriction.</returns>
    private static bool TryRestriction(int effect, out int flag, out BattleScheduledEventKind expiry)
    {
        flag = effect == BattleTechniqueEffect.NoSwitch ? FighterFlags.NoSwitch : FighterFlags.NoDigivolve;
        expiry = effect == BattleTechniqueEffect.NoSwitch ? BattleScheduledEventKind.NoSwitchEnd : BattleScheduledEventKind.NoDigivolveEnd;
        return effect is BattleTechniqueEffect.NoSwitch or BattleTechniqueEffect.NoDigivolve;
    }

    /// <summary>Maps a lowering effect to the stat it changes.</summary>
    /// <param name="effect">Extracted technique effect.</param>
    /// <param name="stat">Target stat when supported.</param>
    /// <returns>Whether the effect lowers a supported stat.</returns>
    private static bool TryLoweredStat(int effect, out int stat)
    {
        stat = effect == BattleTechniqueEffect.LowerAttack ? BattleStat.Attack : BattleStat.Defense;
        return effect is BattleTechniqueEffect.LowerAttack or BattleTechniqueEffect.LowerDefense;
    }

    /// <summary>Raises one of the first two persistent statuses after selecting it before the chance roll.</summary>
    /// <param name="target">Active player whose persistent status is changed.</param>
    /// <param name="chance">Base chance before the wisdom adjustment.</param>
    /// <param name="power">Additive status value.</param>
    /// <param name="enemy">Active enemy supplying the wisdom adjustment.</param>
    /// <param name="rng">Shared random stream.</param>
    /// <returns>Whether the selected status increased.</returns>
    private static bool RaiseOne(BattleFighter target, int chance, int power, BattleFighter enemy, IBattleRng rng)
    {
        int index = rng.NextValue() % 2;
        if (!RollStatusRaise(chance, target, enemy, rng))
        {
            return false;
        }

        target.PersistentStatuses[index] += power;
        return true;
    }

    /// <summary>Rolls and raises each persistent status independently.</summary>
    /// <param name="target">Active player whose persistent statuses are changed.</param>
    /// <param name="chance">Base chance before the wisdom adjustment.</param>
    /// <param name="power">Additive status value.</param>
    /// <param name="enemy">Active enemy supplying the wisdom adjustment.</param>
    /// <param name="rng">Shared random stream.</param>
    /// <returns>Whether at least one status increased.</returns>
    private static bool RaiseEach(BattleFighter target, int chance, int power, BattleFighter enemy, IBattleRng rng)
    {
        bool raised = false;
        for (int index = 0; index < target.PersistentStatuses.Length; index++)
        {
            if (RollStatusRaise(chance, target, enemy, rng))
            {
                target.PersistentStatuses[index] += power;
                raised = true;
            }
        }

        return raised;
    }

    /// <summary>Raises every persistent status after one qualified status chance roll.</summary>
    /// <param name="target">Active player whose persistent statuses are changed.</param>
    /// <param name="chance">Base chance before the wisdom adjustment.</param>
    /// <param name="power">Additive status value.</param>
    /// <param name="enemy">Active enemy supplying the wisdom adjustment.</param>
    /// <param name="rng">Shared random stream.</param>
    /// <returns>Whether every status increased.</returns>
    private static bool RaiseAll(BattleFighter target, int chance, int power, BattleFighter enemy, IBattleRng rng)
    {
        if (!RollStatusRaise(chance, target, enemy, rng))
        {
            return false;
        }

        for (int index = 0; index < target.PersistentStatuses.Length; index++)
        {
            target.PersistentStatuses[index] += power;
        }

        return true;
    }

    /// <summary>Rolls the source status chance with the active partners' wisdom delta.</summary>
    /// <param name="chance">Base chance before the wisdom adjustment.</param>
    /// <param name="target">Active player supplying the positive wisdom term.</param>
    /// <param name="enemy">Active enemy supplying the negative wisdom term.</param>
    /// <param name="rng">Shared random stream.</param>
    /// <returns>Whether the adjusted chance roll succeeded.</returns>
    private static bool RollStatusRaise(int chance, BattleFighter target, BattleFighter enemy, IBattleRng rng) =>
        (rng.NextValue() & 0x7F) < chance + ((target.Stats.Stats[BattleStat.Wisdom] - enemy.Stats.Stats[BattleStat.Wisdom]) / 8);

    /// <summary>Maps a restriction expiry event to the flag it clears.</summary>
    /// <param name="kind">Scheduled event kind.</param>
    /// <param name="flag">Flag to clear when the event is supported.</param>
    /// <returns>Whether the event ends a restriction.</returns>
    private static bool TryRestrictionEnd(BattleScheduledEventKind kind, out int flag)
    {
        flag = kind == BattleScheduledEventKind.NoSwitchEnd ? FighterFlags.NoSwitch : FighterFlags.NoDigivolve;
        return kind is BattleScheduledEventKind.NoSwitchEnd or BattleScheduledEventKind.NoDigivolveEnd;
    }

    /// <summary>Calculates the source restriction duration from its extracted multiplier.</summary>
    /// <param name="power">Extracted duration multiplier.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Delay in battle-time units.</returns>
    private static int RestrictionDelay(int power, IBattleRng rng) => ((rng.NextValue() % 101) + 100) * power;

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
        if (TryRestrictionEnd(scheduled.Kind, out int flag))
        {
            if (fighter is not null)
            {
                fighter.Flags &= ~flag;
            }

            return;
        }

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
