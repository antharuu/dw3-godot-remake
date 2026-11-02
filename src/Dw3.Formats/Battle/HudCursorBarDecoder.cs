using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// Decodes the battle menu cursor strip stored as entry 0 of FILE_BATTLE_IMAGES (file 0x456).
/// </summary>
/// <remarks>
/// The file starts with two u32 entry offsets. Entry 0 is a direct-16 TIM without CLUT: magic
/// 0x10 at +0, image block from +8 with its width in halfwords at +16, its height at +18 and
/// pixels from +20. A pixel word of 0 is transparent.
/// </remarks>
public static class HudCursorBarDecoder
{
    // Cursor bar (entry 0 of FILE_BATTLE_IMAGES): 12 frames of 12×12, strip 156×12.
    /// <summary>Number of cursor animation frames in the strip, including the off-cursor at index 12.</summary>
    public const int CursorFrames = 12;
    /// <summary>Width of one cursor frame in pixels (12 pixels, matching the 12×12 grid).</summary>
    public const int CursorW = 12;
    /// <summary>Height of one cursor frame in pixels (12 pixels, matching the 12×12 grid).</summary>
    public const int CursorH = 12;

    /// <summary>
    /// Decodes the cursor bar TIM (entry 0 of FILE_BATTLE_IMAGES) into RGBA pixels.
    /// Returns (width, height, rgbaPixels). The strip is 156×12 with 12 frames of 12×12.
    /// The last frame (index 12) is the "off" cursor.
    /// </summary>
    /// <param name="imagesData">Raw bytes of FILE_BATTLE_IMAGES (file 0x456).</param>
    /// <returns>Tuple of the decoded strip width in pixels, height in pixels, and RGBA pixel data (4 bytes per pixel).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="imagesData"/> is null.</exception>
    /// <exception cref="FormatException">The archive offsets are invalid or the TIM is malformed.</exception>
    public static (int Width, int Height, byte[] Rgba) Decode(byte[] imagesData)
    {
        ArgumentNullException.ThrowIfNull(imagesData);
        if (imagesData.Length < 8)
        {
            throw new FormatException("FILE_BATTLE_IMAGES is too short.");
        }

        int entry0Off = (int)BinaryPrimitives.ReadUInt32LittleEndian(imagesData.AsSpan());
        int entry1Off = (int)BinaryPrimitives.ReadUInt32LittleEndian(imagesData.AsSpan(4));
        if (entry0Off < 4 || entry1Off <= entry0Off || entry1Off > imagesData.Length)
        {
            throw new FormatException("FILE_BATTLE_IMAGES cursor bar archive offsets are invalid.");
        }

        // Direct-16 TIM: no CLUT, image block starts at byte 8.
        ReadOnlySpan<byte> tim = imagesData.AsSpan(entry0Off, entry1Off - entry0Off);
        if (tim.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(tim) != 0x10)
        {
            throw new FormatException("Battle HUD cursor bar is not a direct-16 TIM.");
        }

        ushort words = BinaryPrimitives.ReadUInt16LittleEndian(tim.Slice(16));
        ushort height = BinaryPrimitives.ReadUInt16LittleEndian(tim.Slice(18));
        int pixelCount = words * height;
        if (tim.Length < 20 + (pixelCount * 2))
        {
            throw new FormatException("Battle HUD cursor bar TIM pixel data is truncated.");
        }

        if (words < (CursorFrames + 1) * CursorW || height != CursorH)
        {
            throw new FormatException($"Battle HUD cursor strip is {words}×{height}, expected at least {(CursorFrames + 1) * CursorW}×{CursorH}.");
        }

        return (words, height, ConvertPixels(tim.Slice(20), pixelCount));
    }

    /// <summary>Expands <paramref name="pixelCount"/> direct-16 pixel words to RGBA.</summary>
    /// <param name="pixels">Pixel words, little-endian, row-major.</param>
    /// <param name="pixelCount">Number of words to convert.</param>
    /// <returns>RGBA bytes; word 0 stays fully transparent, every other word is opaque.</returns>
    private static byte[] ConvertPixels(ReadOnlySpan<byte> pixels, int pixelCount)
    {
        var rgba = new byte[pixelCount * 4];
        for (int i = 0; i < pixelCount; i++)
        {
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(pixels.Slice(i * 2));
            if (word != 0)
            {
                HudColor.WriteRgb(rgba, i * 4, word);
                rgba[(i * 4) + 3] = 255;
            }
        }

        return rgba;
    }
}
