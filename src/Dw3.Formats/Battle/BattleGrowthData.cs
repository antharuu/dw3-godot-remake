namespace Dw3.Formats.Battle;

/// <summary>Growth thresholds and gains stored consecutively in the fight-report overlay.</summary>
public sealed record BattleGrowthData(
    IReadOnlyList<int> LevelExperience,
    IReadOnlyList<int> VitalCuts,
    IReadOnlyList<int> VitalRandom,
    IReadOnlyList<IReadOnlyList<int>> StatGains,
    IReadOnlyList<int> ResistanceGains);
