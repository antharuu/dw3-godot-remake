namespace Dw3Production.Simulation;

/// <summary>One deferred automatic HP recovery requested by item-tech 0xBD.</summary>
/// <param name="Side">Side that owns the deferred recovery.</param>
/// <param name="FighterSlot">Party slot that receives it when the scheduler executes it.</param>
/// <param name="TechniqueId">The source technique that queued the recovery.</param>
public readonly record struct BattleAutoRecovery(BattleSide Side, int FighterSlot, int TechniqueId);
