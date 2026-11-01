namespace Dw3.Formats.Encounter;

/// <summary>
/// A single enemy entry in the FIELDSTG_enemies table (FIELDSTG.PRO).
/// Binary layout: fighter s32 + level s16 + hp s16 + mp s16 + strength s16 = 12 bytes.
/// </summary>
public sealed record BattleEnemy(int Index, int Fighter, short Level, short Hp, short Mp, short Strength);

/// <summary>
/// One of 3 enemy slots within an Encounter record: carries the enemy table index plus its
/// resolved BattleEnemy snapshot.
/// </summary>
public sealed record EncounterEnemySlot(int EnemyIndex, BattleEnemy Enemy);

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

/// <summary>
/// One battle within a BattleList: encounter index, fight stage, and music identifier.
/// Decoded from 12-byte AreaBattle records in WSTAG*.PRO overlays.
/// </summary>
public sealed record AreaBattle(int Encounter, int Stage, int Music, string MusicName);

/// <summary>
/// A list of up to 8 area battles with a shared step-rate index.
/// Binary layout: count s32 + 8 AreaBattle* pointers = 36 bytes.
/// </summary>
public sealed record BattleList(int Count, IReadOnlyList<AreaBattle> Battles);

/// <summary>
/// One area entry in a stage's FieldBattles array.
/// Binary layout: serial s32 + id s32 + pad8 s32 + 4 BattleList* pointers = 28 bytes.
/// </summary>
public sealed record FieldBattles(int Serial, int Id, int Pad8, IReadOnlyList<BattleList> Areas);

/// <summary>
/// All FieldBattles arrays discovered in one stage overlay, with their overlay RAM address
/// and resolved entry count.
/// </summary>
public sealed record StageBattleArray(uint Address, int Count, IReadOnlyList<FieldBattles> Places);

/// <summary>
/// The fully decoded encounter domain for one supported disc: enemies, encounters, battle rates,
/// and per-stage battle arrays.
/// </summary>
public sealed record EncounterManifest(
    IReadOnlyList<BattleEnemy> Enemies,
    IReadOnlyList<EncounterRecord> Encounters,
    IReadOnlyList<int> BattleRates,
    IReadOnlyDictionary<int, IReadOnlyList<StageBattleArray>> Stages);
