namespace Dw3.Formats.Encounter;

/// <summary>
/// A single encounter record in the FIELDSTG_encounters table (FIELDSTG.PRO).
/// Binary layout: 3 BattleEnemy* pointers (4 bytes each) + ambushChance u8 + kind u8 +
/// blocks[12] u8 + 2 bytes padding = 28 bytes total.
/// </summary>
public sealed record EncounterRecord(
    int Index,
    IReadOnlyList<int> EnemyIndices,
    IReadOnlyList<EncounterEnemySlot> Slots,
    byte AmbushChance,
    byte Kind,
    IReadOnlyList<byte> Blocks);
