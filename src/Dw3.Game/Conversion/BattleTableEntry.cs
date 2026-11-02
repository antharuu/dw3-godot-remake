namespace Dw3Production.Conversion;

/// <summary>One SDIGIEDT.PRO BattleTableEntry: an enemy's stats, resistances, techniques and AI actions.</summary>
public sealed record BattleTableEntry(int Id, int Item, int ItemChance, int NameId, IReadOnlyList<int> Techs, IReadOnlyList<int> Stats, IReadOnlyList<int> Resist, int Family, IReadOnlyList<BattleAction> Actions, BattleAction Counter);
