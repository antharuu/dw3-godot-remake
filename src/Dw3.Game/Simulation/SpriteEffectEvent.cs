namespace Dw3Production.Simulation;

/// <summary>A two-dimensional sprite effect requested by the script.</summary>
/// <param name="AttackerSide">Side that owns the script.</param>
/// <param name="Arguments">Raw operand words in source order.</param>
public sealed record SpriteEffectEvent(BattleSide AttackerSide, IReadOnlyList<short> Arguments) : BattleSequenceEvent;
