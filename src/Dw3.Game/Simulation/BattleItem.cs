namespace Dw3Production.Simulation;

/// <summary>
/// One battle-usable item, as the simulation core consumes it (BattleItemData
/// in engine-free form). Effect is the battle item's behaviour key ("heal", "revive",
/// "restoreHalfHpMp", "cureStatus", ...); Target is whom the effect acts on;
/// Amount is its power (ItemEffect.amount).
/// </summary>
public sealed record BattleItem(int Id, string Effect, string Target, int Amount);
