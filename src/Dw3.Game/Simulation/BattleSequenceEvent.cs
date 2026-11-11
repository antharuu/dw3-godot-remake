namespace Dw3Production.Simulation;

/// <summary>A typed event emitted by <see cref="BattleActionTimeline"/> for one script step.
/// Consumers handle the events that belong to their presentation layer and ignore the rest.</summary>
public abstract record BattleSequenceEvent;
