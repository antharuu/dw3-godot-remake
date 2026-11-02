namespace Dw3Production.Conversion;

/// <summary>The dialogue font: per-code glyph and icon metrics of style 1, its two atlas page
/// paths, its line height (14), every style and the directory of the palette pages.</summary>
public sealed record FontData(IReadOnlyDictionary<int, GlyphMetric> Glyphs, IReadOnlyDictionary<int, GlyphMetric> Icons, string Page0Path, string Page1Path, int LineHeight, IReadOnlyDictionary<int, FontStyleData> Styles, string FontDirectory)
{
    /// <summary>Returns the font style for the given style id (1, 2 or 3); throws when the style is absent from the data pack.</summary>
    /// <param name="style">The style id: 1 = field dialogue, 2 = battle, 3 = small.</param>
    /// <returns>The <see cref="FontStyleData"/> for the given style.</returns>
    /// <exception cref="CacheResourceException">The style is not in the data pack.</exception>
    public FontStyleData Style(int style) => Styles.TryGetValue(style, out var data)
        ? data : throw new CacheResourceException("CACHE_INVALID", $"font style missing: {style}");

    /// <summary>Resolves the on-disk path of a palette PNG for a given style, palette and page.</summary>
    /// <param name="style">The font style id (1–3).</param>
    /// <param name="palette">The CLUT palette index (0–7).</param>
    /// <param name="page">The atlas page index (0–1).</param>
    /// <returns>The absolute path to the PNG file.</returns>
    /// <exception cref="CacheResourceException">The arguments are out of range or the file is absent.</exception>
    public string PalettePagePath(int style, int palette, int page)
    {
        if (style is < 1 or > 3 || palette is < 0 or > 7 || page is < 0 or > 1)
        {
            throw new CacheResourceException("CACHE_INVALID", $"font palette path invalid: style={style} palette={palette} page={page}");
        }

        var path = Path.Combine(FontDirectory, $"font-style-{style}-palette-{palette}-page-{page}.png");
        return File.Exists(path) ? path : throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"font palette page missing: {path}");
    }
}
