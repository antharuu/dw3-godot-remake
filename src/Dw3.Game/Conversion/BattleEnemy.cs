namespace Dw3Production.Conversion;

/// <summary>One FIELDSTG_enemies entry: a battle-ready enemy at a fixed level/HP/MP.</summary>
public sealed record BattleEnemy(int Index, int Fighter, int Level, int Hp, int Mp, int Strength);
