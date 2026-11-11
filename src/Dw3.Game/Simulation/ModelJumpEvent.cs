namespace Dw3Production.Simulation;

/// <summary>A source model jump request carrying the actor, kind, and optional kind-four distance.</summary>
/// <param name="AttackerSide">Side that owns the action script.</param>
/// <param name="Actor">Raw actor selector from the action script.</param>
/// <param name="Kind">Source jump kind.</param>
/// <param name="Distance">Authored source distance, meaningful only for kind four.</param>
public sealed record ModelJumpEvent(BattleSide AttackerSide, int Actor, int Kind, int Distance) : BattleSequenceEvent;
