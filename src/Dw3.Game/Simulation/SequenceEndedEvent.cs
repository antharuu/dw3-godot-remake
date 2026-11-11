namespace Dw3Production.Simulation;

/// <summary>The script reached its End or AlternateEnd opcode: all effects have been issued.</summary>
/// <param name="AttackerSide">Side that owned the script.</param>
/// <param name="AttackerId">Fighter id of the attacker.</param>
/// <param name="TargetId">Fighter id of the target.</param>
public sealed record SequenceEndedEvent(BattleSide AttackerSide, int AttackerId, int TargetId) : BattleSequenceEvent;
