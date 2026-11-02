namespace Dw3Production.Conversion;

/// <summary>One BattleList: the battle-rate index and its eight area battles.</summary>
public sealed record EncounterBattleList(int Count, IReadOnlyList<EncounterAreaBattle> Battles);
