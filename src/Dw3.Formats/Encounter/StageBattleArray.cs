namespace Dw3.Formats.Encounter;

/// <summary>
/// All FieldBattles arrays discovered in one stage overlay, with their overlay RAM address
/// and resolved entry count.
/// </summary>
public sealed record StageBattleArray(uint Address, int Count, IReadOnlyList<FieldBattles> Places);
