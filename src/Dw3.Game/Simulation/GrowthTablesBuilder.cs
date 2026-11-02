using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// Copies the growth tables decoded from the data pack into the fixed-size runtime
/// <see cref="GrowthTables"/>. Each runtime table keeps the size the battle rules index with;
/// a shorter decoded table leaves the remaining slots at zero and a longer one is truncated.
/// </summary>
internal static class GrowthTablesBuilder
{
    /// <summary>Builds the runtime growth tables.</summary>
    /// <param name="data">Growth tables read from the data pack.</param>
    /// <returns>The runtime tables.</returns>
    public static GrowthTables Build(GrowthTablesData data)
    {
        var tables = new GrowthTables();
        CopyInto(tables.LevelExp, data.LevelExp);
        CopyInto(tables.VitalCuts, data.VitalCuts);
        CopyInto(tables.VitalRandom, data.VitalRandom);
        for (var tier = 0; tier < tables.StatGains.Length && tier < data.StatGains.Count; tier++)
        {
            CopyInto(tables.StatGains[tier], data.StatGains[tier]);
        }

        CopyInto(tables.ResistGains, data.ResistGains);
        return tables;
    }

    /// <summary>Copies the overlapping prefix of a decoded table into a runtime table.</summary>
    /// <param name="target">Runtime table, whose length is fixed.</param>
    /// <param name="source">Decoded values.</param>
    private static void CopyInto(int[] target, IReadOnlyList<int> source)
    {
        for (var i = 0; i < target.Length && i < source.Count; i++)
        {
            target[i] = source[i];
        }
    }
}
