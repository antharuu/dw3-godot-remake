namespace Dw3Production.Simulation;

/// <summary>The script's HitDispatch opcode fired: the renderer should now apply the pre-resolved hit visuals.</summary>
/// <param name="AttackerSide">Side that initiated the action.</param>
/// <param name="AttackerId">Fighter id of the attacker, frozen at sequence start.</param>
/// <param name="TargetId">Fighter id of the target, frozen at sequence start.</param>
/// <param name="HitEvents">Pre-resolved events from the single RNG draw; must not be re-rolled.</param>
/// <param name="DispatchIndex">Raw dispatch index argument from the script.</param>
public sealed record HitDispatchedEvent(
    BattleSide AttackerSide,
    int AttackerId,
    int TargetId,
    IReadOnlyList<BattleHitEvent> HitEvents,
    int DispatchIndex) : BattleSequenceEvent;
