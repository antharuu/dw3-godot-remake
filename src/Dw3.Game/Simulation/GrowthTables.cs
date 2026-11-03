namespace Dw3Production.Simulation;

/// <summary>Holds the lookup values used to grow a partner after battle experience.</summary>
public sealed class GrowthTables
{
    /// <summary>Gets or sets the cumulative experience base for each level tier.</summary>
    public int[] LevelExp { get; set; } = new int[4];

    /// <summary>Gets or sets the HP and MP reduction for each vital tier.</summary>
    public int[] VitalCuts { get; set; } = new int[4];

    /// <summary>Gets or sets the random HP and MP gain adjustments.</summary>
    public int[] VitalRandom { get; set; } = new int[9];

    /// <summary>Gets or sets the per-tier stat gain lookup rows.</summary>
    public int[][] StatGains { get; set; } = CreateStatGainRows();

    /// <summary>Gets or sets the resistance gain lookup values.</summary>
    public int[] ResistGains { get; set; } = new int[8];

    /// <summary>Allocates the six stat gain rows (tier 0 plus the five tiers starting at levels 5, 20, 40, 60 and 80), nine values each, filled with zeros until the pack values are copied in.</summary>
    /// <returns>A new 6 × 9 jagged array.</returns>
    private static int[][] CreateStatGainRows()
    {
        var rows = new int[6][];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = new int[9];
        }

        return rows;
    }
}
