namespace Dw3Production.Simulation;

/// <summary>Owns the bounded relative-time battle event queue and EU equal-time selection rule.</summary>
public sealed class BattleTurnScheduler
{
    private const int Capacity = 99;
    private readonly BattleScheduledEvent?[] _events = new BattleScheduledEvent?[Capacity];

    /// <summary>Gets elapsed battle-time units removed from popped events.</summary>
    public int Elapsed { get; private set; }

    /// <summary>Gets the number of pending events.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the delay until the next selected event is due without consuming it.</summary>
    public int NextDueDelay => EventAt(FindNextIndex()).Delay;

    /// <summary>Gets the kind of the next queued event without consuming it.</summary>
    public BattleScheduledEventKind NextKind => EventAt(FindNextIndex()).Kind;

    /// <summary>Clears the queue and initializes the normal or ambush opening.</summary>
    /// <param name="playerSpeed">Active player speed.</param>
    /// <param name="enemySpeed">Active enemy speed.</param>
    /// <param name="ambushChance">Encounter ambush chance, zero when disabled.</param>
    /// <param name="rng">Injected shared battle random source.</param>
    public void InitializeOpening(int playerSpeed, int enemySpeed, int ambushChance, IBattleRng rng)
        => InitializeOpening(playerSpeed, enemySpeed, ambushChance, playerLevel: 1, enemyLevel: 1, rng);

    /// <summary>Clears the queue and initializes the normal or source-level-scaled ambush opening.</summary>
    /// <param name="playerSpeed">Active player speed.</param>
    /// <param name="enemySpeed">Active enemy speed.</param>
    /// <param name="ambushChance">Raw encounter ambush chance, zero when disabled.</param>
    /// <param name="playerLevel">Level used by the source ambush scaling.</param>
    /// <param name="enemyLevel">Enemy level used by the source ambush scaling.</param>
    /// <param name="rng">Injected shared battle random source.</param>
    public void InitializeOpening(int playerSpeed, int enemySpeed, int ambushChance, int playerLevel, int enemyLevel, IBattleRng rng)
    {
        Reset();
        int playerDelay = BattleActionDelay.Calculate(BattleActionDelayKind.NormalTurn, playerSpeed, enemySpeed, 0, rng);
        if (BattleTurnSchedulerRules.IsAmbush(ambushChance, playerLevel, enemyLevel, rng))
        {
            EnqueueTurn(BattleSide.Enemy, 0);
            EnqueueTurn(BattleSide.Player, playerDelay / 2);
            return;
        }

        bool enemyFirst = (rng.NextValue() % 128) < (Math.Max(enemySpeed, 1) * 8 / Math.Max(playerSpeed, 1));
        EnqueueOpeningTurns(enemyFirst, playerDelay);
    }

    /// <summary>Enqueues one ordinary side turn, replacing that side's pending turn.</summary>
    /// <param name="side">Side receiving the turn.</param>
    /// <param name="delay">Relative delay before that turn.</param>
    public void EnqueueTurn(BattleSide side, int delay)
    {
        RemovePendingTurn(side);
        Add(new BattleScheduledEvent(BattleTurnSchedulerRules.TurnKind(side), side, Math.Max(delay, 0)));
    }

    /// <summary>Calculates and enqueues one action-family turn.</summary>
    /// <param name="side">Side receiving the next turn.</param>
    /// <param name="kind">Qualified action delay family.</param>
    /// <param name="ownSpeed">Speed of that side.</param>
    /// <param name="otherSpeed">Speed of its opponent.</param>
    /// <param name="spirit">Spirit of that side.</param>
    /// <param name="rng">Injected shared battle random source.</param>
    public void EnqueueActionTurn(BattleSide side, BattleActionDelayKind kind, int ownSpeed, int otherSpeed, int spirit, IBattleRng rng) =>
        EnqueueTurn(side, BattleActionDelay.Calculate(kind, ownSpeed, otherSpeed, spirit, rng));

    /// <summary>Inserts an immediate event before every pending event.</summary>
    /// <param name="kind">Immediate event class.</param>
    /// <param name="side">Side associated with the event.</param>
    /// <param name="requestedDelay">The requested priority delay, normally one.</param>
    /// <param name="fighterSlot">Fighter affected by the priority event, or -1 for a side-wide event.</param>
    public void EnqueuePriority(BattleScheduledEventKind kind, BattleSide side, int requestedDelay, int fighterSlot = -1)
    {
        int shift = Math.Max(requestedDelay, 2);
        ShiftAll(shift);
        Add(new BattleScheduledEvent(kind, side, 1, fighterSlot));
    }

    /// <summary>Queues the active partner's source-priority forced return to its base form.</summary>
    /// <param name="fighterSlot">Active player slot selected when the enemy action landed.</param>
    public void EnqueueDedigivolve(int fighterSlot) =>
        EnqueuePriority(BattleScheduledEventKind.Dedigivolve, BattleSide.Player, requestedDelay: 1, fighterSlot);

    /// <summary>Creates or refreshes one fighter's pending poison damage event.</summary>
    /// <param name="side">Side containing the poisoned fighter.</param>
    /// <param name="fighterSlot">Slot of the poisoned fighter.</param>
    /// <param name="power">Stored poison strength.</param>
    public void EnqueuePoison(BattleSide side, int fighterSlot, int power)
    {
        Remove(BattleScheduledEventKind.PoisonDamage, side, fighterSlot);
        Add(new BattleScheduledEvent(BattleScheduledEventKind.PoisonDamage, side, 1000, fighterSlot, power));
    }

    /// <summary>Removes a fighter's pending poison damage event.</summary>
    /// <param name="side">Side containing the cured or knocked-out fighter.</param>
    /// <param name="fighterSlot">Slot of the fighter.</param>
    public void RemovePoison(BattleSide side, int fighterSlot) =>
        Remove(BattleScheduledEventKind.PoisonDamage, side, fighterSlot);

    /// <summary>Creates or refreshes a control-status expiry for one fighter.</summary>
    /// <param name="kind">Control-status expiry kind.</param>
    /// <param name="side">Side containing the affected fighter.</param>
    /// <param name="fighterSlot">Slot of the affected fighter.</param>
    /// <param name="delay">Qualified delay until the status ends.</param>
    public void EnqueueControlExpiry(BattleScheduledEventKind kind, BattleSide side, int fighterSlot, int delay)
    {
        Remove(kind, side, fighterSlot);
        Add(new BattleScheduledEvent(kind, side, Math.Max(delay, 0), fighterSlot));
    }

    /// <summary>Creates or refreshes the pending expiry of the battle's temporary field boost.</summary>
    /// <param name="previousBoost">Field boost to restore when the expiry is due.</param>
    /// <param name="delay">Qualified delay until the field returns to its previous value.</param>
    public void EnqueueElementBoostExpiry(ElementBoost previousBoost, int delay)
    {
        ElementBoost originalBoost = PendingElementBoostRestore() ?? previousBoost;
        Remove(BattleScheduledEventKind.ElementBoostEnd, BattleSide.Player, fighterSlot: -1);
        Add(new BattleScheduledEvent(BattleScheduledEventKind.ElementBoostEnd, BattleSide.Player, Math.Max(delay, 0), PreviousBoost: originalBoost));
    }

    /// <summary>Removes one control-status expiry for a fighter.</summary>
    /// <param name="kind">Control-status expiry kind.</param>
    /// <param name="side">Side containing the affected fighter.</param>
    /// <param name="fighterSlot">Slot of the affected fighter.</param>
    public void RemoveControlExpiry(BattleScheduledEventKind kind, BattleSide side, int fighterSlot) =>
        Remove(kind, side, fighterSlot);

    /// <summary>Pops the next event using the slot-sensitive EU tie rule.</summary>
    /// <returns>The selected event with its pre-pop relative delay.</returns>
    public BattleScheduledEvent PopNext()
    {
        int index = FindNextIndex();
        var next = EventAt(index);
        Elapsed += next.Delay;
        ReduceAll(next.Delay);
        _events[index] = null;
        Count--;
        return next;
    }

    /// <summary>Gets the side of the next queued turn without mutating elapsed time.</summary>
    /// <returns>The next turn side.</returns>
    public BattleSide PeekNextTurnSide() => EventAt(FindNextIndex()).Side;

    /// <summary>Gets the delay remaining before a side's pending turn.</summary>
    /// <param name="side">Side to inspect.</param>
    /// <returns>Relative delay, or zero when no turn is pending.</returns>
    public int RemainingTurnDelay(BattleSide side) =>
        _events.FirstOrDefault(item => item?.Kind == BattleTurnSchedulerRules.TurnKind(side)).GetValueOrDefault().Delay;

    /// <summary>Clears events and elapsed simulation time.</summary>
    private void Reset()
    {
        Array.Clear(_events);
        Count = 0;
        Elapsed = 0;
    }

    /// <summary>Gets the original field value retained by a pending refreshed field effect.</summary>
    /// <returns>The retained original field value, or <see langword="null"/> when none is pending.</returns>
    private ElementBoost? PendingElementBoostRestore()
    {
        foreach (BattleScheduledEvent? candidate in _events)
        {
            if (candidate is { Kind: BattleScheduledEventKind.ElementBoostEnd } pending)
            {
                return pending.PreviousBoost;
            }
        }

        return null;
    }

    /// <summary>Queues both opening turns after their single opening decision.</summary>
    /// <param name="enemyFirst">Whether the enemy begins.</param>
    /// <param name="playerDelay">The active player's normal family delay.</param>
    private void EnqueueOpeningTurns(bool enemyFirst, int playerDelay)
    {
        EnqueueTurn(enemyFirst ? BattleSide.Enemy : BattleSide.Player, 0);
        EnqueueTurn(enemyFirst ? BattleSide.Player : BattleSide.Enemy, playerDelay / 2);
    }

    /// <summary>Finds the next event while preserving the EU later-turn tie replacement rule.</summary>
    /// <returns>Index of the selected event.</returns>
    private int FindNextIndex()
    {
        if (Count == 0)
        {
            throw new InvalidOperationException("Battle event queue is empty.");
        }

        int best = Array.FindIndex(_events, item => item.HasValue);
        for (var index = best + 1; index < Capacity; index++)
        {
            if (_events[index] is { } candidate && BattleTurnSchedulerRules.ShouldReplaceBest(EventAt(best), candidate))
            {
                best = index;
            }
        }

        return best;
    }

    /// <summary>Removes the pending ordinary turn of one side.</summary>
    /// <param name="side">Side whose turn is replaced.</param>
    private void RemovePendingTurn(BattleSide side)
    {
        for (var index = 0; index < Capacity; index++)
        {
            if (_events[index]?.Kind == BattleTurnSchedulerRules.TurnKind(side))
            {
                _events[index] = null;
                Count--;
            }
        }
    }

    /// <summary>Removes all events matching one per-fighter status identity.</summary>
    /// <param name="kind">Kind of event to remove.</param>
    /// <param name="side">Affected side.</param>
    /// <param name="fighterSlot">Affected fighter slot.</param>
    private void Remove(BattleScheduledEventKind kind, BattleSide side, int fighterSlot)
    {
        for (var index = 0; index < Capacity; index++)
        {
            if (_events[index] is { } item && item.Kind == kind && item.Side == side && item.FighterSlot == fighterSlot)
            {
                _events[index] = null;
                Count--;
            }
        }
    }

    /// <summary>Adds time to every pending event for a priority insertion.</summary>
    /// <param name="shift">Units added to pending events.</param>
    private void ShiftAll(int shift) => ReplaceDelays(item => item.Delay + shift);

    /// <summary>Subtracts elapsed units from pending events after a pop.</summary>
    /// <param name="elapsed">Units removed from every remaining event.</param>
    private void ReduceAll(int elapsed) => ReplaceDelays(item => item.Delay - elapsed);

    /// <summary>Rewrites all event delays through one transformation.</summary>
    /// <param name="transform">Delay transformation.</param>
    private void ReplaceDelays(Func<BattleScheduledEvent, int> transform)
    {
        for (var index = 0; index < Capacity; index++)
        {
            if (_events[index] is { } item)
            {
                _events[index] = item with { Delay = transform(item) };
            }
        }
    }

    /// <summary>Reads an occupied slot without allowing holes to become default events.</summary>
    /// <param name="index">Selected occupied slot.</param>
    /// <returns>The event in that slot.</returns>
    private BattleScheduledEvent EventAt(int index) =>
        _events[index] ?? throw new InvalidOperationException("Battle event slot is empty.");

    /// <summary>Adds one event to the first free slot without relocating existing events.</summary>
    /// <param name="item">Event to add.</param>
    private void Add(BattleScheduledEvent item)
    {
        int free = Array.FindIndex(_events, entry => !entry.HasValue);
        if (free < 0)
        {
            throw new InvalidOperationException("Battle event queue capacity exceeded.");
        }

        _events[free] = item;
        Count++;
    }
}
