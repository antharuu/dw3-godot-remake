namespace Dw3.Formats.Text;

/// <summary>
/// Decoded metrics for one <c>TextStyle</c> descriptor.
/// </summary>
/// <remarks>
/// The active European styles have baseline distances of 14, 11, and 9 pixels. They share
/// the two atlas pages and differ only in glyph geometry and CLUT origin.
/// </remarks>
/// <param name="LineHeight">Pixels between successive text baselines.</param>
/// <param name="Glyphs">Glyph metrics indexed by token code minus four.</param>
/// <param name="Icons">Icon metrics indexed by token code minus one.</param>
public sealed record FontStyleMetrics(int LineHeight, IReadOnlyList<GlyphMetric> Glyphs, IReadOnlyList<GlyphMetric> Icons);
