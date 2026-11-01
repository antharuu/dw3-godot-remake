namespace Dw3.Formats.Encounter;

/// <summary>
/// One area entry in a stage's FieldBattles array.
/// Binary layout: serial s32 + id s32 + pad8 s32 + 4 BattleList* pointers = 28 bytes.
/// </summary>
public sealed record FieldBattles(int Serial, int Id, int Pad8, IReadOnlyList<BattleList> Areas);
