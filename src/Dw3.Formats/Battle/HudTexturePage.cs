namespace Dw3.Formats.Battle;

/// <summary>
/// One decoded 4-bit texture page: 256 rows of 64 halfwords (= 256×256 texels).
/// Stored as row-major; row[y][w] is the w-th halfword at row y.
/// </summary>
public sealed record HudTexturePage(IReadOnlyList<IReadOnlyList<ushort>> Rows)
{
    /// <summary>Number of rows (always 256 for a PS1 texture page).</summary>
    public int Height => Rows.Count;
    /// <summary>Halfwords per row (always 64 for a 4-bit page).</summary>
    public int WidthWords => Rows.Count > 0 ? Rows[0].Count : 0;
}
