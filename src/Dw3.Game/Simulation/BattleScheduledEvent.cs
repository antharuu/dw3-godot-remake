namespace Dw3Production.Simulation;

/// <summary>One relative-time battle event retained by <see cref="BattleTurnScheduler"/>.</summary>
/// <param name="Kind">Executable event class.</param>
/// <param name="Side">Side associated with the event.</param>
/// <param name="Delay">Remaining battle-time units until execution.</param>
/// <param name="FighterSlot">Fighter slot affected by a per-fighter event, or -1 for a side-wide event.</param>
/// <param name="Value">Effect-specific stored value.</param>
public readonly record struct BattleScheduledEvent(
    BattleScheduledEventKind Kind,
    BattleSide Side,
    int Delay,
    int FighterSlot = -1,
    int Value = 0);
