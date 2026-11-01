using System.Diagnostics;

namespace Dw3.Formats.Battle;

/// <summary>
/// Cuts one 256×256 texture out of a model's local VRAM and converts it to RGBA through its CLUT.
/// </summary>
/// <remarks>
/// A page starts at VRAM column Page × 0x40 and spans 64, 128 or 256 halfwords per row for the
/// 4-bit, 8-bit and 15-bit modes. 4-bit words hold four texels (low nibble first), 8-bit words two.
/// Colour word 0 is transparent; the STP bit (0x8000) gives alpha 128, anything else 255. Channels
/// expand from 5 to 8 bits as (v &lt;&lt; 3) | (v &gt;&gt; 2).
/// </remarks>
internal static class BattleTexturePageDecoder
{
    private const int PageSize = 256;
    private const int PageColumnStride = 0x40;

    /// <summary>Decodes the texture selected by <paramref name="key"/>.</summary>
    /// <param name="vram">Rebuilt local VRAM of the model's texture archive.</param>
    /// <param name="key">Mode, page and CLUT of the texture.</param>
    /// <param name="index">Texture key assigned to the combination.</param>
    /// <returns>The decoded texture with its transparent and STP pixel counts.</returns>
    /// <exception cref="FormatException">The page or the CLUT lies outside the local VRAM.</exception>
    public static BattleTexture Decode(BattleLocalVram vram, BattleTextureCombination key, int index)
    {
        int wordsPerRow = key.Mode switch { 0 => 64, 1 => 128, 2 => 256, _ => throw new UnreachableException() };
        int pageX = key.Page * PageColumnStride;
        if (pageX < 0 || pageX + wordsPerRow > vram.Width || vram.Height < PageSize)
        {
            throw new FormatException($"texture page {key.Page} mode {key.Mode} outside local VRAM archive");
        }

        ushort[]? palette = ReadPalette(vram, key);
        int samples = key.Mode switch { 0 => 4, 1 => 2, _ => 1 };
        byte[] rgba = new byte[PageSize * PageSize * 4];
        int transparent = 0;
        int stp = 0;
        int pixel = 0;
        for (int y = 0; y < PageSize; y++)
        {
            for (int wordIndex = 0; wordIndex < wordsPerRow; wordIndex++)
            {
                ushort word = vram.Words[(y * vram.Width) + pageX + wordIndex];
                for (int sample = 0; sample < samples; sample++)
                {
                    ushort color = SampleColor(word, sample, key.Mode, palette);
                    WritePixel(rgba, pixel++ * 4, color, ref transparent, ref stp);
                }
            }
        }

        return new BattleTexture(index, key.Mode, key.Page, key.ClutX, key.ClutY, PageSize, PageSize, transparent, stp, rgba);
    }

    /// <summary>Copies the 16- or 256-entry CLUT of a paletted texture out of the local VRAM.</summary>
    /// <param name="vram">Local VRAM.</param>
    /// <param name="key">Texture combination; its CLUT is set for modes 0 and 1.</param>
    /// <returns>The palette words, or null for 15-bit direct colour.</returns>
    /// <exception cref="FormatException">The CLUT lies outside the local VRAM.</exception>
    private static ushort[]? ReadPalette(BattleLocalVram vram, BattleTextureCombination key)
    {
        if (key.Mode is not (0 or 1))
        {
            return null;
        }

        int size = key.Mode == 0 ? 16 : 256;
        int cx = key.ClutX!.Value;
        int cy = key.ClutY!.Value;
        if (cy < 0 || cy >= vram.Height || cx < 0 || cx + size > vram.Width)
        {
            throw new FormatException($"CLUT ({cx},{cy}) outside local VRAM archive");
        }

        return vram.Words.AsSpan((cy * vram.Width) + cx, size).ToArray();
    }

    /// <summary>Returns the colour word of texel <paramref name="sample"/> of a VRAM word.</summary>
    /// <param name="word">VRAM halfword.</param>
    /// <param name="sample">Texel within the word: 0..3 for 4-bit, 0..1 for 8-bit, 0 for direct colour.</param>
    /// <param name="mode">Tpage colour mode.</param>
    /// <param name="palette">CLUT for the paletted modes.</param>
    /// <returns>The PS1 colour word.</returns>
    private static ushort SampleColor(ushort word, int sample, int mode, ushort[]? palette) => mode switch
    {
        0 => palette![(word >> (sample * 4)) & 0xF],
        1 => palette![(word >> (sample * 8)) & 0xFF],
        _ => word,
    };

    /// <summary>Writes one texel and counts transparent (word 0) and semi-transparent (STP bit) texels.</summary>
    /// <param name="rgba">Destination RGBA buffer.</param>
    /// <param name="at">Byte offset of the texel.</param>
    /// <param name="color">PS1 colour word.</param>
    /// <param name="transparent">Running count of transparent texels.</param>
    /// <param name="stp">Running count of texels with the STP bit.</param>
    private static void WritePixel(byte[] rgba, int at, ushort color, ref int transparent, ref int stp)
    {
        if (color == 0)
        {
            transparent++;
            return;
        }

        bool semi = (color & 0x8000) != 0;
        rgba[at] = Expand5(color & 31);
        rgba[at + 1] = Expand5((color >> 5) & 31);
        rgba[at + 2] = Expand5((color >> 10) & 31);
        rgba[at + 3] = (byte)(semi ? 128 : 255);
        if (semi)
        {
            stp++;
        }
    }

    /// <summary>Expands a 5-bit channel to 8 bits by replicating its top bits.</summary>
    /// <param name="value">Channel value, 0..31.</param>
    /// <returns>The 8-bit channel, 0..255.</returns>
    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));
}
