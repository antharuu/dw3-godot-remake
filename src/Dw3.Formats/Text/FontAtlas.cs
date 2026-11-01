namespace Dw3.Formats.Text;

/// <summary>
/// Decoded CMFONTTM.BIN font atlas with palette data and indexed TIM pages.
/// </summary>
public sealed record FontAtlas(
    /// <summary>Eight 16-entry RGBA palettes read from the first page's in-page CLUT.</summary>
    IReadOnlyList<IReadOnlyList<(byte R, byte G, byte B, byte A)>> Palettes,
    /// <summary>Raw pages stored as 4-bit-indexed pixels.</summary>
    IReadOnlyList<FontAtlasPage> Pages,
    /// <summary>SHA-256 hex digest of the source archive.</summary>
    string SourceSha256);
