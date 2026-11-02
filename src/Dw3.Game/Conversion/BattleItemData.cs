namespace Dw3Production.Conversion;

/// <summary>One battle-usable item: its disc-decoded flags/kind/amount and price, the
/// code-derived battle effect and target (the item-record contract), and its FSITMNAM name token stream.</summary>
public sealed record BattleItemData(int Id, int NameId, IReadOnlyList<TextToken> Name, int Flags, int EffectKind, int Amount, int Price, int SellPrice, int Type, int Category, string Effect, string Target);
