namespace Dw3Production.Conversion;

/// <summary>The m6-battle-item-v1 contract: every battle-usable item.</summary>
public sealed record BattleItemsData(IReadOnlyList<BattleItemData> Items);
