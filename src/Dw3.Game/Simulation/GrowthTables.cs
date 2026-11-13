namespace Dw3Production.Simulation;

/// <summary>Holds the lookup values used to grow a partner after battle experience.</summary>
public sealed class GrowthTables
{
    /// <summary>Width of one stat gain row, the original's nine columns.</summary>
    private const int StatGainRowWidth = 9;

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

    /// <summary>
    /// Reads one stat gain as the original indexes its contiguous six-row table: a column past a row's end continues
    /// into the next row, and past the last row into the resistance gains, since the original does not bound the
    /// growth plus random column.
    /// </summary>
    /// <param name="tier">Stat tier row, 0..5.</param>
    /// <param name="column">Growth plus random column, 0 or more.</param>
    /// <returns>The value the original reads at that position.</returns>
    public int StatGain(int tier, int column)
    {
        int flat = (tier * StatGainRowWidth) + column;
        int rowCount = StatGains.Length;
        return flat < rowCount * StatGainRowWidth ? StatGains[flat / StatGainRowWidth][flat % StatGainRowWidth] : ResistGain(flat - (rowCount * StatGainRowWidth));
    }

    /// <summary>
    /// Reads one resistance gain as the original does without a bound. A resistance growth of 5 reaches past the
    /// table, which ends the report overlay, and live memory there was observed as zeros in the original's report.
    /// </summary>
    /// <param name="index">Growth plus random index, 0 or more.</param>
    /// <returns>The table value, or 0 past its end.</returns>
    public int ResistGain(int index) => index < ResistGains.Length ? ResistGains[index] : 0;

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
