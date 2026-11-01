namespace Dw3.Formats.Text;

/// <summary>
/// Decoded <c>CMFONTTM.BIN</c> font atlas with palette data and indexed TIM pages.
/// </summary>
/// <param name="Palettes">Eight 16-entry RGBA palettes read from page zero's embedded CLUT.</param>
/// <param name="Pages">Decompressed pages stored as 4-bit indexed pixels.</param>
/// <param name="SourceSha256">SHA-256 digest of the source archive bytes.</param>
public sealed record FontAtlas(
    IReadOnlyList<IReadOnlyList<(byte R, byte G, byte B, byte A)>> Palettes,
    IReadOnlyList<FontAtlasPage> Pages,
    string SourceSha256);
