using System.Buffers.Binary;

namespace Dw3.Formats.Binary.Tim;

/// <summary>
/// Decodes PSX TIM images to RGBA pixel data.
///
/// Supported TIM flags:
/// <list type="bullet">
///   <item>0x08 — 4-bit indexed colour with CLUT (16 entries per row)</item>
///   <item>0x09 — 8-bit indexed colour with CLUT (256 entries per row)</item>
///   <item>0x02 — 15-bit direct colour, no CLUT</item>
/// </list>
///
/// TIM file layout (all integers are little-endian):
/// <code>
///   offset  0 : uint32  magic = 0x00000010
///   offset  4 : uint32  flags  (pixel mode | CLUT flag)
///
///   if flags bit 3 (CLUT present):
///     offset  8 : uint32  clut_block_length  (includes this 4-byte length field)
///     offset 12 : uint16  clut_vram_x
///     offset 14 : uint16  clut_vram_y
///     offset 16 : uint16  num_colors   (16 for 4-bit, 256 for 8-bit)
///     offset 18 : uint16  num_rows
///     offset 20 : uint16[num_colors * num_rows]  colour entries (15-bit BGR)
///     offset  8 + clut_block_length : image block
///
///   image block at img_base:
///     img_base+ 0 : uint32  img_block_length  (includes this 4-byte length field)
///     img_base+ 4 : uint16  vram_x
///     img_base+ 6 : uint16  vram_y
///     img_base+ 8 : uint16  words_per_line
///     img_base+10 : uint16  height
///     img_base+12 : pixel data  [img_block_length - 12 bytes]
///
///   pixel width:
///     4-bit  → words_per_line * 4
///     8-bit  → words_per_line * 2
///     16-bit → words_per_line
///
///   4-bit nibble order: low nibble = left pixel (pixel 0 = byte &amp; 0xF, pixel 1 = byte >> 4)
/// </code>
///
/// Transparency: word 0x0000 maps to RGBA (0,0,0,0); all other words map to
/// fully opaque RGBA (alpha = 255) regardless of STP bit 15.
/// This matches all production Python decoders.
/// </summary>
public static class TimDecoder
{
    private const uint TimMagic = 0x00000010u;

    // Supported flags values
    private const uint Flags4BitClut = 0x08u;   // 4-bit indexed + CLUT
    private const uint Flags8BitClut = 0x09u;   // 8-bit indexed + CLUT
    private const uint Flags16BitDirect = 0x02u; // 15-bit direct, no CLUT

    /// <summary>
    /// Decodes a TIM image, using CLUT row index <paramref name="clutRow"/> for
    /// indexed modes.
    /// </summary>
    /// <param name="data">Raw TIM bytes (not RLEN-compressed).</param>
    /// <param name="clutRow">
    /// Zero-based CLUT row index to use for indexed modes (4-bit or 8-bit).
    /// Ignored for 16-bit direct TIMs.  Defaults to 0.
    /// </param>
    /// <returns>Decoded RGBA image.</returns>
    /// <exception cref="FormatException">Any structural violation.</exception>
    public static TimImage Decode(ReadOnlySpan<byte> data, int clutRow = 0)
    {
        if (data.Length < 8)
        {
            throw new FormatException(
                $"TIM data too short ({data.Length} bytes; need at least 8)");
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        if (magic != TimMagic)
        {
            throw new FormatException(
                $"TIM magic mismatch: expected 0x{TimMagic:X8}, got 0x{magic:X8}");
        }

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data[4..8]);

        return flags switch
        {
            Flags4BitClut => Decode4Bit(data, clutRow),
            Flags8BitClut => Decode8Bit(data, clutRow),
            Flags16BitDirect => Decode16Bit(data),
            _ => throw new FormatException(
                $"Unsupported TIM flags 0x{flags:X8}. " +
                $"Supported: 0x08 (4-bit+CLUT), 0x09 (8-bit+CLUT), 0x02 (16-bit direct).")
        };
    }

    // ── 4-bit indexed ──────────────────────────────────────────────────────

    /// <summary>Decodes a 4-bit indexed TIM: reads the CLUT at offset 8, expands nibble-packed pixels using row <paramref name="clutRow"/>.</summary>
    /// <param name="data">Full TIM byte buffer (flags already verified as 0x08).</param>
    /// <param name="clutRow">Zero-based CLUT row index (0 to numRows-1).</param>
    /// <returns>Decoded RGBA image with mode <see cref="TimPixelMode.Indexed4Bit"/>.</returns>
    private static TimImage Decode4Bit(ReadOnlySpan<byte> data, int clutRow)
    {
        // CLUT block at offset 8
        if (data.Length < 12)
        {
            throw new FormatException("4-bit TIM CLUT block truncated");
        }

        uint clutBlockLen = BinaryPrimitives.ReadUInt32LittleEndian(data[8..12]);
        if (clutBlockLen < 12 || (long)8 + clutBlockLen > data.Length)
        {
            throw new FormatException(
                $"4-bit TIM CLUT block length {clutBlockLen} is invalid");
        }

        int numColors = BinaryPrimitives.ReadUInt16LittleEndian(data[16..18]);
        int numRows = BinaryPrimitives.ReadUInt16LittleEndian(data[18..20]);

        if (numColors != 16)
        {
            throw new FormatException(
                $"4-bit TIM CLUT must have 16 colours per row, got {numColors}");
        }

        if (numRows < 1)
        {
            throw new FormatException("4-bit TIM CLUT has no rows");
        }

        if (20 + numColors * numRows * 2 > 8 + (int)clutBlockLen)
        {
            throw new FormatException(
                "4-bit TIM CLUT palette data exceeds declared CLUT block length");
        }

        if (clutRow < 0 || clutRow >= numRows)
        {
            throw new FormatException(
                $"CLUT row {clutRow} is out of range (0..{numRows - 1})");
        }

        int paletteOffset = 20 + clutRow * numColors * 2;
        var palette = ReadPalette(data, paletteOffset, numColors);

        // Image block immediately after CLUT block
        int imgBase = 8 + (int)clutBlockLen;
        var (width, height, pixelData) = ReadImageBlock(data, imgBase, widthMultiplier: 4);

        if (pixelData.Length != width * height / 2)
            throw new FormatException(
                $"4-bit TIM pixel data is {pixelData.Length} bytes, " +
                $"expected {width * height / 2} for {width}x{height}");

        // Expand nibble-packed pixels (low nibble = left pixel)
        var pixels = new byte[width * height * 4];
        int outPos = 0;
        foreach (byte b in pixelData)
        {
            WriteRgba(pixels, outPos, palette[b & 0x0F]);
            outPos += 4;
            WriteRgba(pixels, outPos, palette[b >> 4]);
            outPos += 4;
        }

        return new TimImage(width, height, pixels, TimPixelMode.Indexed4Bit, numRows);
    }

    // ── 8-bit indexed ──────────────────────────────────────────────────────

    /// <summary>Decodes an 8-bit indexed TIM: reads the CLUT at offset 8, maps each byte to an RGBA palette entry using row <paramref name="clutRow"/>.</summary>
    /// <param name="data">Full TIM byte buffer (flags already verified as 0x09).</param>
    /// <param name="clutRow">Zero-based CLUT row index (0 to numRows-1).</param>
    /// <returns>Decoded RGBA image with mode <see cref="TimPixelMode.Indexed8Bit"/>.</returns>
    private static TimImage Decode8Bit(ReadOnlySpan<byte> data, int clutRow)
    {
        if (data.Length < 12)
        {
            throw new FormatException("8-bit TIM CLUT block truncated");
        }

        uint clutBlockLen = BinaryPrimitives.ReadUInt32LittleEndian(data[8..12]);
        if (clutBlockLen < 12 || (long)8 + clutBlockLen > data.Length)
        {
            throw new FormatException(
                $"8-bit TIM CLUT block length {clutBlockLen} is invalid");
        }

        int numColors = BinaryPrimitives.ReadUInt16LittleEndian(data[16..18]);
        int numRows = BinaryPrimitives.ReadUInt16LittleEndian(data[18..20]);

        if (numColors != 256)
        {
            throw new FormatException(
                $"8-bit TIM CLUT must have 256 colours per row, got {numColors}");
        }

        if (numRows < 1)
        {
            throw new FormatException("8-bit TIM CLUT has no rows");
        }

        if (20 + numColors * numRows * 2 > 8 + (int)clutBlockLen)
        {
            throw new FormatException(
                "8-bit TIM CLUT palette data exceeds declared CLUT block length");
        }

        if (clutRow < 0 || clutRow >= numRows)
        {
            throw new FormatException(
                $"CLUT row {clutRow} is out of range (0..{numRows - 1})");
        }

        int paletteOffset = 20 + clutRow * numColors * 2;
        var palette = ReadPalette(data, paletteOffset, numColors);

        int imgBase = 8 + (int)clutBlockLen;
        var (width, height, pixelData) = ReadImageBlock(data, imgBase, widthMultiplier: 2);

        if (pixelData.Length != width * height)
        {
            throw new FormatException(
                $"8-bit TIM pixel data is {pixelData.Length} bytes, " +
                $"expected {width * height} for {width}x{height}");
        }

        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixelData.Length; i++)
        {
            WriteRgba(pixels, i * 4, palette[pixelData[i]]);
        }

        return new TimImage(width, height, pixels, TimPixelMode.Indexed8Bit, numRows);
    }

    // ── 16-bit direct ──────────────────────────────────────────────────────

    /// <summary>Decodes a 15-bit direct-colour TIM: no CLUT; image block starts at offset 8; each 16-bit word is converted via <see cref="Rgba5551.ToRgba"/>.</summary>
    /// <param name="data">Full TIM byte buffer (flags already verified as 0x02).</param>
    /// <returns>Decoded RGBA image with mode <see cref="TimPixelMode.Direct15Bit"/> and zero CLUT rows.</returns>
    private static TimImage Decode16Bit(ReadOnlySpan<byte> data)
    {
        // No CLUT block; image block starts immediately at offset 8
        var (width, height, pixelData) = ReadImageBlock(data, imgBase: 8, widthMultiplier: 1);

        int wordCount = width * height;
        if (pixelData.Length != wordCount * 2)
            throw new FormatException(
                $"16-bit TIM pixel data is {pixelData.Length} bytes, " +
                $"expected {wordCount * 2} for {width}x{height}");

        var pixels = new byte[wordCount * 4];
        for (int i = 0; i < wordCount; i++)
        {
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(pixelData.AsSpan(i * 2, 2));
            WriteRgba(pixels, i * 4, Rgba5551.ToRgba(word));
        }

        return new TimImage(width, height, pixels, TimPixelMode.Direct15Bit, clutRowCount: 0);
    }

    // ── Shared helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Reads the image block at <paramref name="imgBase"/> and returns
    /// (pixel_width, height, raw_pixel_bytes).
    /// </summary>
    /// <param name="data">Full TIM byte buffer.</param>
    /// <param name="imgBase">Byte offset at which the image block starts (immediately after the CLUT block, or 8 for 16-bit TIMs).</param>
    /// <param name="widthMultiplier">Factor applied to <c>words_per_line</c> to derive pixel width: 4 for 4-bit, 2 for 8-bit, 1 for 16-bit.</param>
    /// <returns>A tuple of (pixel width, height in lines, raw pixel bytes copied from the block).</returns>
    private static (int Width, int Height, byte[] PixelData) ReadImageBlock(
        ReadOnlySpan<byte> data, int imgBase, int widthMultiplier)
    {
        if (imgBase < 0 || imgBase + 12 > data.Length)
            throw new FormatException(
                $"TIM image block header at 0x{imgBase:X} exceeds buffer");

        uint imgBlockLen = BinaryPrimitives.ReadUInt32LittleEndian(data[imgBase..(imgBase + 4)]);
        if (imgBlockLen < 12 || (long)imgBase + imgBlockLen > data.Length)
            throw new FormatException(
                $"TIM image block length {imgBlockLen} at 0x{imgBase:X} is invalid");

        int wordsPerLine = BinaryPrimitives.ReadUInt16LittleEndian(data[(imgBase + 8)..(imgBase + 10)]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data[(imgBase + 10)..(imgBase + 12)]);

        if (wordsPerLine == 0 || height == 0)
            throw new FormatException(
                $"TIM image block at 0x{imgBase:X} has zero dimensions ({wordsPerLine} words x {height})");

        int width = wordsPerLine * widthMultiplier;
        int pixelDataLen = (int)imgBlockLen - 12;

        if (pixelDataLen < 0)
            throw new FormatException(
                $"TIM image block at 0x{imgBase:X} has no pixel data");

        return (width, height, data[(imgBase + 12)..(imgBase + 12 + pixelDataLen)].ToArray());
    }

    /// <summary>Reads <paramref name="count"/> 15-bit BGR colour words as RGBA tuples.</summary>
    /// <param name="data">Full TIM byte buffer containing the palette entries.</param>
    /// <param name="offset">Byte offset of the first colour word within <paramref name="data"/>.</param>
    /// <param name="count">Number of colour words to read (16 for 4-bit CLUTs, 256 for 8-bit CLUTs).</param>
    /// <returns>An array of <paramref name="count"/> RGBA tuples in palette index order.</returns>
    private static (byte R, byte G, byte B, byte A)[] ReadPalette(
        ReadOnlySpan<byte> data, int offset, int count)
    {
        var palette = new (byte R, byte G, byte B, byte A)[count];
        for (int i = 0; i < count; i++)
        {
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + i * 2)..(offset + i * 2 + 2)]);
            palette[i] = Rgba5551.ToRgba(word);
        }

        return palette;
    }

    /// <summary>Writes the four RGBA bytes of <paramref name="color"/> at <paramref name="offset"/> in <paramref name="dest"/>.</summary>
    /// <param name="dest">RGBA output array allocated to hold all pixels.</param>
    /// <param name="offset">Byte position of the first channel (R); must leave room for 4 bytes.</param>
    /// <param name="color">RGBA tuple to write.</param>
    private static void WriteRgba(byte[] dest, int offset, (byte R, byte G, byte B, byte A) color)
    {
        dest[offset] = color.R;
        dest[offset + 1] = color.G;
        dest[offset + 2] = color.B;
        dest[offset + 3] = color.A;
    }
}
