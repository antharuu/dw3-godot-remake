namespace Dw3Production.Conversion;

/// <summary>One of the executable's three font styles. It is data, not a fallback policy:
/// callers must explicitly choose style 1, 2 or 3.</summary>
public sealed record FontStyleData(IReadOnlyDictionary<int, GlyphMetric> Glyphs, IReadOnlyDictionary<int, GlyphMetric> Icons, int LineHeight);
