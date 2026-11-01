namespace Dw3Production.Simulation;

/// <summary>
/// A Digimon's growth profile, the per-Digimon columns that a level-up reads from its data
/// entry. The growth values are row offsets into <see cref="GrowthTables"/>: each gain is the
/// table value at the growth offset plus a random swing.
/// </summary>
/// <param name="ExpRate">The experience curve's multiplier, in tenths.</param>
/// <param name="HpGrowth">The max HP added per level before the vital cut and swing.</param>
/// <param name="MpGrowth">The max MP added per level before the vital cut and swing.</param>
/// <param name="StatGrowth">One stat gain offset per battle stat, in the battle-stat order (six entries).</param>
/// <param name="ResistGrowth">One resistance gain offset per element (seven entries).</param>
public sealed record PartnerGrowthCurve(
    int ExpRate,
    int HpGrowth,
    int MpGrowth,
    IReadOnlyList<int> StatGrowth,
    IReadOnlyList<int> ResistGrowth);
