namespace Dw3.Formats.Binary.Tim;

/// <summary>
/// Converts PSX 15-bit colour words (5-5-5 BGR) to 32-bit RGBA.
///
/// PSX transparency convention used by disc files in this project:
/// <list type="bullet">
///   <item>
///     <c>word == 0x0000</c> → fully transparent (R=0, G=0, B=0, A=0).
///   </item>
///   <item>
///     <c>word != 0x0000</c> → fully opaque (A=255).
///     Bit 15 (the STP semi-transparency flag) is NOT placed in the alpha
///     channel; it is preserved only in the <c>IsStp</c> helper for callers
///     that track semi-transparency separately.
///   </item>
/// </list>
///
/// This matches the behaviour of all Python production decoders
/// (<c>ps1_color</c> in field_packets, <c>color</c> in player_cells, and
/// the inline loop in battle_model).
/// </summary>
public static class Rgba5551
{
    /// <summary>
    /// Converts a single PSX 15-bit BGR colour word to RGBA.
    /// </summary>
    /// <param name="word">15-bit BGR colour word (bits 4..0 = R, 9..5 = G, 14..10 = B, 15 = STP flag).</param>
    /// <returns>
    /// <c>(R, G, B, A)</c> where A is 0 for the transparent sentinel
    /// (<paramref name="word"/> == 0) and 255 for all other values.
    /// </returns>
    public static (byte R, byte G, byte B, byte A) ToRgba(ushort word)
    {
        if (word == 0)
        {
            return (0, 0, 0, 0);
        }

        // 5-bit components are scaled to 0–255 using integer arithmetic
        // that matches the Python formula: (component * 255) / 31.
        byte r = (byte)((word & 0x1F) * 255 / 31);
        byte g = (byte)(((word >> 5) & 0x1F) * 255 / 31);
        byte b = (byte)(((word >> 10) & 0x1F) * 255 / 31);
        return (r, g, b, 255);
    }

    /// <summary>
    /// Returns true when bit 15 of <paramref name="word"/> is set (STP flag).
    /// Only meaningful when the word is non-zero (zero is transparent, not STP).
    /// </summary>
    /// <param name="word">15-bit BGR colour word to test.</param>
    /// <returns><see langword="true"/> when the STP bit (bit 15) is set and the word is non-zero.</returns>
    public static bool IsStp(ushort word) => word != 0 && (word >> 15) != 0;

    /// <summary>
    /// Converts an array of PSX 15-bit BGR colour words to packed RGBA bytes
    /// (4 bytes per pixel: R, G, B, A).
    /// </summary>
    /// <param name="words">Span of 15-bit BGR colour words to convert.</param>
    /// <returns>A byte array of length <c>words.Length * 4</c> in R, G, B, A order.</returns>
    public static byte[] PaletteToRgba(ReadOnlySpan<ushort> words)
    {
        var rgba = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; i++)
        {
            var (r, g, b, a) = ToRgba(words[i]);
            int j = i * 4;
            rgba[j] = r;
            rgba[j + 1] = g;
            rgba[j + 2] = b;
            rgba[j + 3] = a;
        }

        return rgba;
    }
}
