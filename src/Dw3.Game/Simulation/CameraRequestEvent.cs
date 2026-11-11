namespace Dw3Production.Simulation;

/// <summary>A camera move with timing and mode parameters requested by the script.</summary>
/// <param name="AttackerSide">Side that owns the script.</param>
/// <param name="Arguments">Raw operand words in source order (mode, timing, and optional trajectory).</param>
public sealed record CameraRequestEvent(BattleSide AttackerSide, IReadOnlyList<short> Arguments) : BattleSequenceEvent;
