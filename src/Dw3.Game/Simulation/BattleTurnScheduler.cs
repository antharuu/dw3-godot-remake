namespace Dw3Production.Simulation;

/// <summary>Owns the bounded relative-time battle event queue and EU equal-time selection rule.</summary>
public sealed class BattleTurnScheduler
{
    private const int Capacity = 99;
    private readonly List<BattleScheduledEvent> _events = [];

    /// <summary>Gets elapsed battle-time units removed from popped events.</summary>
    public int Elapsed { get; private set; }

    /// <summary>Gets the number of pending events.</summary>
    public int Count => _events.Count;

    /// <summary>Gets the delay until the next selected event is due without consuming it.</summary>
    public int NextDueDelay => _events[FindNextIndex()].Delay;

    /// <summary>Clears the queue and initializes the normal or ambush opening.</summary>
    /// <param name="playerSpeed">Active player speed.</param>
    /// <param name="enemySpeed">Active enemy speed.</param>
    /// <param name="ambushChance">Encounter ambush chance, zero when disabled.</param>
    /// <param name="rng">Injected shared battle random source.</param>
    public void InitializeOpening(int playerSpeed, int enemySpeed, int ambushChance, IBattleRng rng)
    {
        Reset();
        int playerDelay = BattleActionDelay.Calculate(BattleActionDelayKind.NormalTurn, playerSpeed, enemySpeed, 0, rng);
        if (BattleTurnSchedulerRules.IsAmbush(ambushChance, rng))
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
    public void EnqueuePriority(BattleScheduledEventKind kind, BattleSide side, int requestedDelay)
    {
        int shift = Math.Max(requestedDelay, 2);
        ShiftAll(shift);
        Add(new BattleScheduledEvent(kind, side, 1));
    }

    /// <summary>Pops the next event using the slot-sensitive EU tie rule.</summary>
    /// <returns>The selected event with its pre-pop relative delay.</returns>
    public BattleScheduledEvent PopNext()
    {
        int index = FindNextIndex();
        var next = _events[index];
        Elapsed += next.Delay;
        ReduceAll(next.Delay);
        _events.RemoveAt(index);
        return next;
    }

    /// <summary>Gets the side of the next queued turn without mutating elapsed time.</summary>
    /// <returns>The next turn side.</returns>
    public BattleSide PeekNextTurnSide() => _events[FindNextIndex()].Side;

    /// <summary>Gets the delay remaining before a side's pending turn.</summary>
    /// <param name="side">Side to inspect.</param>
    /// <returns>Relative delay, or zero when no turn is pending.</returns>
    public int RemainingTurnDelay(BattleSide side) => _events.FirstOrDefault(item => item.Kind == BattleTurnSchedulerRules.TurnKind(side)).Delay;

    /// <summary>Clears events and elapsed simulation time.</summary>
    private void Reset()
    {
        _events.Clear();
        Elapsed = 0;
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
        if (_events.Count == 0)
        {
            throw new InvalidOperationException("Battle event queue is empty.");
        }

        int best = 0;
        for (var index = 1; index < _events.Count; index++)
        {
            if (BattleTurnSchedulerRules.ShouldReplaceBest(_events[best], _events[index]))
            {
                best = index;
            }
        }

        return best;
    }

    /// <summary>Removes the pending ordinary turn of one side.</summary>
    /// <param name="side">Side whose turn is replaced.</param>
    private void RemovePendingTurn(BattleSide side) => _events.RemoveAll(item => item.Kind == BattleTurnSchedulerRules.TurnKind(side));

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
        for (var index = 0; index < _events.Count; index++)
        {
            var item = _events[index];
            _events[index] = item with { Delay = transform(item) };
        }
    }

    /// <summary>Adds one event while enforcing the source queue capacity.</summary>
    /// <param name="item">Event to add.</param>
    private void Add(BattleScheduledEvent item)
    {
        if (_events.Count >= Capacity)
        {
            throw new InvalidOperationException("Battle event queue capacity exceeded.");
        }

        _events.Add(item);
    }
}
