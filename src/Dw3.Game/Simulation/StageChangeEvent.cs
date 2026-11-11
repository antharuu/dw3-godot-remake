namespace Dw3Production.Simulation;

/// <summary>A battle stage load or fade transition requested by the script.</summary>
/// <param name="Arguments">Raw operand words in source order.</param>
public sealed record StageChangeEvent(IReadOnlyList<short> Arguments) : BattleSequenceEvent;
