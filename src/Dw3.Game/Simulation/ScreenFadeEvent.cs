namespace Dw3Production.Simulation;

/// <summary>Preserves a decoded screen-fade request for its explicit rendering owner.</summary>
/// <param name="Arguments">Source fade mode and duration.</param>
public sealed record ScreenFadeEvent(IReadOnlyList<short> Arguments) : BattleSequenceEvent;
