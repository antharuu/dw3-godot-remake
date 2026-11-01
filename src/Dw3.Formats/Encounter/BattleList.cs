namespace Dw3.Formats.Encounter;

/// <summary>
/// A list of up to 8 area battles with a shared step-rate index.
/// Binary layout: count s32 + 8 AreaBattle* pointers = 36 bytes.
/// </summary>
public sealed record BattleList(int Count, IReadOnlyList<AreaBattle> Battles);
