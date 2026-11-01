namespace Dw3.Formats.Battle;

/// <summary>One fighter-table entry: a fighter id, its record index and its kind.</summary>
/// <param name="FighterId">Battle fighter id (signed 16-bit on disc).</param>
/// <param name="Index">Record index inside the partner or enemy table.</param>
/// <param name="Kind">Kind byte; kinds at or above <see cref="FighterModelTable.EnemyKindMinimum"/> are enemies.</param>
public sealed record FighterModelEntry(short FighterId, byte Index, byte Kind);
