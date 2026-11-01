namespace Dw3.Formats.Text;

/// <summary>
/// One decompressed 4-bit TIM page from the font archive.
/// </summary>
public sealed record FontAtlasPage(
    int Width,
    int Height,
    /// <summary>4-bit indexed pixels where the low nibble precedes the high nibble.</summary>
    byte[] IndexedPixels);
