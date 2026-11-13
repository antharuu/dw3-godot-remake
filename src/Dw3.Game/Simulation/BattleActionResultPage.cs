namespace Dw3Production.Simulation;

/// <summary>
/// One page of the action result message the source shows once an action's script ends: the target's name,
/// then its damage (message type 4), its per-hit damage, landed hits and total (type 0x10), or a miss (type 2).
/// </summary>
/// <param name="Key">Battle text catalogue message key: <c>damage</c>, <c>multiHitDamage</c> or <c>missed</c>.</param>
/// <param name="TargetSide">Side of the named fighter, the one the result applies to.</param>
/// <param name="TargetId">Fighter id named on the first line.</param>
/// <param name="Numbers">Values printed by the second line, in argument order.</param>
public sealed record BattleActionResultPage(string Key, BattleSide TargetSide, int TargetId, IReadOnlyList<int> Numbers)
{
    /// <summary>Gets the first printed number, the damage shown by a single-hit message.</summary>
    public int Amount => Numbers.Count > 0 ? Numbers[0] : 0;
}
