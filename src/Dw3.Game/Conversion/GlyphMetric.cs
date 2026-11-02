namespace Dw3Production.Conversion;

/// <summary>One glyph/icon metric row: the atlas source rectangle plus draw offset and advance.</summary>
public sealed record GlyphMetric(int Page, int U, int V, int ClutX, int ClutY, int W, int H, int Dx, int Dy, int Advance);
