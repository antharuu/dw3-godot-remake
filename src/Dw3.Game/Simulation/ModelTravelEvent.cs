namespace Dw3Production.Simulation;

/// <summary>A validated root-travel request, distinct from pose animation.</summary>
/// <param name="AttackerSide">Side owning the script.</param>
/// <param name="Arguments">Source model subcommand, actor and travel operands.</param>
public sealed record ModelTravelEvent(BattleSide AttackerSide, IReadOnlyList<short> Arguments) : BattleSequenceEvent;
