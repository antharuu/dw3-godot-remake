namespace Dw3.Formats.Encounter;

/// <summary>
/// One 12-byte enemy entry from the <c>FIELDSTG_enemies</c> table.
/// </summary>
/// <param name="Index">Zero-based position in the enemy table.</param>
/// <param name="Fighter">Signed 32-bit fighter identifier.</param>
/// <param name="Level">Signed 16-bit battle level.</param>
/// <param name="Hp">Signed 16-bit hit-point value.</param>
/// <param name="Mp">Signed 16-bit magic-point value.</param>
/// <param name="Strength">Signed 16-bit strength value.</param>
public sealed record BattleEnemy(int Index, int Fighter, short Level, short Hp, short Mp, short Strength);
