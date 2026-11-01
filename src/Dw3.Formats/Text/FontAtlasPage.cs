namespace Dw3.Formats.Text;

/// <summary>
/// One decompressed 4-bit TIM page from the font archive.
/// </summary>
/// <param name="Width">Page width in pixels.</param>
/// <param name="Height">Page height in pixels.</param>
/// <param name="IndexedPixels">4-bit pixels whose low nibble precedes the high nibble.</param>
public sealed record FontAtlasPage(int Width, int Height, byte[] IndexedPixels);
