namespace Dw3Production.Simulation;

/// <summary>A three-dimensional effect model load or trigger requested by the script.</summary>
/// <param name="AttackerSide">Side that owns the script.</param>
/// <param name="Arguments">Raw operand words in source order.</param>
public sealed record EffectModelEvent(BattleSide AttackerSide, IReadOnlyList<short> Arguments) : BattleSequenceEvent;
