namespace Dw3.Formats.Encounter;

/// <summary>
/// A single enemy entry in the FIELDSTG_enemies table (FIELDSTG.PRO).
/// Binary layout: fighter s32 + level s16 + hp s16 + mp s16 + strength s16 = 12 bytes.
/// </summary>
public sealed record BattleEnemy(int Index, int Fighter, short Level, short Hp, short Mp, short Strength);
