namespace Dw3Production.Conversion;

/// <summary>The growth tables from STFGTREP.PRO (the growth-table contract): level-exp per tier,
/// the HP/MP vital cuts and random swing, the six battle-stat gain tiers and the
/// resistance gains.</summary>
public sealed record GrowthTablesData(IReadOnlyList<int> LevelExp, IReadOnlyList<int> VitalCuts, IReadOnlyList<int> VitalRandom, IReadOnlyList<IReadOnlyList<int>> StatGains, IReadOnlyList<int> ResistGains);
