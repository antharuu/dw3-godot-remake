using System.Buffers.Binary;

namespace Dw3.Formats.Text;

/// <summary>Renders each font style using its glyph-record CLUT, not the dialogue style's palette.</summary>
public static class FontStyleAtlasRenderer
{
    /// <summary>Resolves one style's consecutive palette row and renders the supplied indexed page.</summary>
    /// <param name="pixels">Indexed font page to render.</param>
    /// <param name="palettePage">Page containing the in-page palette halfwords.</param>
    /// <param name="glyph">Style glyph defining the CLUT origin in halfwords.</param>
    /// <param name="palette">Palette row relative to the style origin, zero through seven.</param>
    /// <returns>The style-correct RGBA page.</returns>
    public static byte[] Render(FontAtlasPage pixels, FontAtlasPage palettePage, GlyphMetric glyph, int palette)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(palettePage);
        ArgumentNullException.ThrowIfNull(glyph);
        ArgumentOutOfRangeException.ThrowIfNegative(palette);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(palette, 7);
        int wordsPerRow = palettePage.Width / 4;
        int row = glyph.ClutY + palette;
        if (glyph.ClutX + 16 > wordsPerRow || row >= palettePage.Height)
        {
            throw new FormatException("Font style CLUT lies outside its indexed page.");
        }

        var colors = new (byte R, byte G, byte B, byte A)[16];
        for (int index = 0; index < colors.Length; index++)
        {
            int offset = ((row * wordsPerRow) + glyph.ClutX + index) * 2;
            if (offset + 2 > palettePage.IndexedPixels.Length)
            {
                throw new FormatException("Font style CLUT data is truncated.");
            }

            colors[index] = FontAtlasDecoder.HalfwordToRgba(BinaryPrimitives.ReadUInt16LittleEndian(palettePage.IndexedPixels.AsSpan(offset, 2)));
        }

        return FontAtlasDecoder.RenderPage(pixels, colors);
    }
}
