namespace Dw3.Formats.Text;

/// <summary>
/// Decoded metrics for one TextStyle: line height, glyph table, and icon table.
///
/// The three active styles carry line heights 14, 11, and 9 pixels respectively.
/// Each style shares the same two font atlas pages from CMFONTTM.BIN; only the
/// glyph geometry and CLUT origin differ across styles.
/// </summary>
public sealed record FontStyleMetrics(
    /// <summary>Pixels between successive text baselines for this style.</summary>
    int LineHeight,
    /// <summary>Glyph metrics indexed 0–(count-1); token glyph code = index + 4.</summary>
    IReadOnlyList<GlyphMetric> Glyphs,
    /// <summary>Icon metrics indexed 0–(count-1); token icon code = index + 1.</summary>
    IReadOnlyList<GlyphMetric> Icons);
