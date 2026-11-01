namespace Dw3.Formats.Text;

/// <summary>
/// One glyph or icon metric record decoded from a TextStyle table in the game executable.
///
/// On disc each record occupies 11 bytes (page, u, v, clutX, clutY, width, height,
/// signed dx, signed dy, advance, unused). The trailing unused byte is discarded.
/// Glyph codes in the token stream start at 4; icon codes start at 1.
/// </summary>
public sealed record GlyphMetric(
    byte Page,
    byte U,
    byte V,
    byte ClutX,
    byte ClutY,
    byte Width,
    byte Height,
    sbyte Dx,
    sbyte Dy,
    byte Advance);
