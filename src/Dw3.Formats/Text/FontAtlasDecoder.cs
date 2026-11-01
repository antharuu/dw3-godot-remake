using System.Buffers.Binary;
using Dw3.Formats.Binary;

namespace Dw3.Formats.Text;

/// <summary>
/// Decodes the CMFONTTM.BIN font atlas archive.
///
/// Archive layout: the raw disc file begins with a 4-byte FILE_CACHE count header;
/// the archive body starts at offset 4. Within the body an offset table of u32
/// values (relative to offset 4) is terminated by a zero entry. Each non-zero
/// offset points to an RLEN-compressed 4-bit TIM page.
///
/// The CLUT is embedded in page 0's own pixel area. Eight palette rows of 16
/// halfwords (15-bit BGR) sit at pixel column 48, rows 232–239. CLUT halfword 0
/// maps to fully transparent RGBA (0,0,0,0); all other entries are fully opaque.
/// </summary>
public static class FontAtlasDecoder
{
    // FILE_CACHE leading count header skipped before the archive body.
    private const int FileCacheHeaderSize = 4;

    // In-page CLUT position (matches FONT_CLUT_X / _Y in the Python decoder).
    private const int ClutColumnX = 48;
    private const int ClutRowY = 232;
    private const int ClutPaletteCount = 8;
    private const int ClutEntriesPerRow = 16;

    /// <summary>
    /// Decodes the font atlas from the raw CMFONTTM.BIN file bytes.
    /// </summary>
    /// <param name="data">Full contents of AAA/DAT/COUNTRY/CMFONTTM.BIN.</param>
    /// <param name="sha256Hex">Pre-computed SHA-256 of <paramref name="data"/> (written to the output manifest).</param>
    /// <returns>The decoded TIM pages and their eight in-page palette rows.</returns>
    /// <exception cref="FormatException">Any archive, RLEN, or TIM structural violation.</exception>
    public static FontAtlas Decode(ReadOnlySpan<byte> data, string sha256Hex)
    {
        if (data.Length < FileCacheHeaderSize + 4)
        {
            throw new FormatException("CMFONTTM.BIN is too short to contain a valid archive header.");
        }

        // The archive body begins at offset 4 (after FILE_CACHE count header).
        // The offset table within the body is a zero-terminated sequence of u32
        // offsets relative to the archive body start.
        int bodyBase = FileCacheHeaderSize;
        var pages = new List<FontAtlasPage>();
        int tableIndex = 0;
        while (true)
        {
            int tablePos = bodyBase + (tableIndex * sizeof(uint));
            if (tablePos + sizeof(uint) > data.Length)
            {
                throw new FormatException("CMFONTTM.BIN archive offset table is truncated.");
            }

            uint entryOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[tablePos..]);
            if (entryOffset == 0)
            {
                break;
            }

            int streamPos = bodyBase + (int)entryOffset;
            if (streamPos + 4 > data.Length)
            {
                throw new FormatException(
                    $"CMFONTTM.BIN archive entry {tableIndex} at offset {streamPos} is outside the file.");
            }

            if (!RlenDecoder.IsRlen(data[streamPos..]))
            {
                throw new FormatException(
                    $"CMFONTTM.BIN archive entry {tableIndex} does not start with RLEN magic.");
            }

            byte[] decompressed = RlenDecoder.Decompress(data[streamPos..]);
            pages.Add(DecodePage(decompressed, tableIndex));
            tableIndex++;
        }

        if (pages.Count < 2)
        {
            throw new FormatException(
                $"CMFONTTM.BIN has fewer than 2 pages ({pages.Count}); at least 2 are required.");
        }

        var palettes = ReadPalettes(pages[0]);
        return new FontAtlas(palettes, pages, sha256Hex);
    }

    /// <summary>Validates and decodes one decompressed 4-bit TIM page from the archive body.</summary>
    /// <param name="timData">RLEN-expanded TIM bytes beginning with the 0x10 magic word.</param>
    /// <param name="pageIndex">Zero-based archive entry index used only in format diagnostics.</param>
    /// <returns>The page dimensions in texels and its packed four-bit pixel bytes.</returns>
    private static FontAtlasPage DecodePage(ReadOnlySpan<byte> timData, int pageIndex)
    {
        // 4-bit TIM: magic 0x10, flags 0x02 (no CLUT block in header; CLUT is in-page).
        if (timData.Length < 8)
        {
            throw new FormatException($"CMFONTTM page {pageIndex} TIM header is truncated.");
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(timData);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(timData[4..]);
        if (magic != 0x10u)
        {
            throw new FormatException($"CMFONTTM page {pageIndex} does not have TIM magic 0x10 (got 0x{magic:X8}).");
        }

        if (flags != 0x02u)
        {
            throw new FormatException($"CMFONTTM page {pageIndex} TIM flags 0x{flags:X8} are not the expected 4-bit 0x02.");
        }

        // Image block starts immediately at offset 8 (no CLUT block).
        if (timData.Length < 8 + 12)
        {
            throw new FormatException($"CMFONTTM page {pageIndex} image block header is truncated.");
        }

        uint blockLen = BinaryPrimitives.ReadUInt32LittleEndian(timData[8..]);
        if (blockLen < 12 || 8 + blockLen > (uint)timData.Length)
        {
            throw new FormatException($"CMFONTTM page {pageIndex} image block length {blockLen} is invalid.");
        }

        ushort words = BinaryPrimitives.ReadUInt16LittleEndian(timData[16..]);
        ushort height = BinaryPrimitives.ReadUInt16LittleEndian(timData[18..]);
        // 4-bit: four pixels per 16-bit halfword → 4 pixels per "word" of 2 bytes → width = words * 4.
        int width = words * 4;
        int expectedPixelBytes = (words * height) * 2;
        if (8 + blockLen - 12 < (uint)expectedPixelBytes)
        {
            throw new FormatException($"CMFONTTM page {pageIndex} pixel data is truncated.");
        }

        byte[] pixels = timData.Slice(8 + 12, expectedPixelBytes).ToArray();

        return new FontAtlasPage(width, height, pixels);
    }

    /// <summary>
    /// Reads the eight 16-entry RGBA palettes embedded in the first atlas page.
    /// </summary>
    /// <param name="page0">First decoded TIM page, whose rows 232 through 239 contain the CLUT words.</param>
    /// <returns>Eight palette rows in source-row order with zero words converted to transparent RGBA.</returns>
    private static IReadOnlyList<IReadOnlyList<(byte R, byte G, byte B, byte A)>> ReadPalettes(FontAtlasPage page0)
    {
        // The page pixels are stored as 16-bit halfwords (words_per_line * height rows).
        // CLUT rows are at pixel column ClutColumnX, rows ClutRowY .. ClutRowY+ClutPaletteCount-1.
        // Each halfword encodes one 15-bit BGR colour plus STP bit 15 (ignored here).
        int wordsPerLine = page0.Width / 4; // page.Width is 4× the halfword count
        // The raw pixel bytes hold each pair of bytes as one 16-bit halfword (LE).
        // Row-major: byte at (row * wordsPerLine + col) * 2.
        if (page0.Width < (ClutColumnX + ClutEntriesPerRow) * 4 ||
            page0.Height < ClutRowY + ClutPaletteCount)
        {
            throw new FormatException(
                $"CMFONTTM page 0 ({page0.Width}×{page0.Height}) is too small to hold the in-page CLUT.");
        }

        var palettes = new List<IReadOnlyList<(byte R, byte G, byte B, byte A)>>(ClutPaletteCount);
        for (int rowIndex = 0; rowIndex < ClutPaletteCount; rowIndex++)
        {
            var entries = new (byte R, byte G, byte B, byte A)[ClutEntriesPerRow];
            for (int col = 0; col < ClutEntriesPerRow; col++)
            {
                int bytePos = (((ClutRowY + rowIndex) * wordsPerLine) + ClutColumnX + col) * 2;
                ushort halfword = BinaryPrimitives.ReadUInt16LittleEndian(page0.IndexedPixels.AsSpan(bytePos, 2));
                entries[col] = HalfwordToRgba(halfword);
            }

            palettes.Add(entries);
        }

        return palettes;
    }

    /// <summary>Converts a 15-bit BGR halfword to RGBA. Word 0 is fully transparent.</summary>
    /// <param name="word">Little-endian TIM colour word with 5-bit B, G and R channels.</param>
    /// <returns>Opaque expanded RGBA channels, or four zero channels when <paramref name="word"/> is zero.</returns>
    public static (byte R, byte G, byte B, byte A) HalfwordToRgba(ushort word)
    {
        if (word == 0)
        {
            return (0, 0, 0, 0);
        }

        byte r = (byte)((word & 0x1F) * 255 / 31);
        byte g = (byte)(((word >> 5) & 0x1F) * 255 / 31);
        byte b = (byte)(((word >> 10) & 0x1F) * 255 / 31);
        return (r, g, b, 255);
    }

    /// <summary>
    /// Renders one atlas page with the given palette row as a flat RGBA byte array.
    /// Pixel order: low nibble = left pixel (index = byte &amp; 0xF), high nibble = right (byte >> 4).
    /// </summary>
    /// <param name="page">Packed four-bit page whose byte nibbles select palette entries.</param>
    /// <param name="palette">The 16-entry RGBA row extracted from the page-zero CLUT.</param>
    /// <returns>Four RGBA bytes per page texel in row-major order.</returns>
    public static byte[] RenderPage(FontAtlasPage page, IReadOnlyList<(byte R, byte G, byte B, byte A)> palette)
    {
        int pixelCount = page.Width * page.Height;
        var rgba = new byte[pixelCount * 4];
        int byteIndex = 0;
        for (int pixelPair = 0; pixelPair < page.IndexedPixels.Length; pixelPair++)
        {
            byte packed = page.IndexedPixels[pixelPair];
            byte lo = (byte)(packed & 0xF);
            byte hi = (byte)(packed >> 4);
            if (byteIndex + 7 < rgba.Length)
            {
                var (r0, g0, b0, a0) = palette[lo];
                rgba[byteIndex++] = r0;
                rgba[byteIndex++] = g0;
                rgba[byteIndex++] = b0;
                rgba[byteIndex++] = a0;
                var (r1, g1, b1, a1) = palette[hi];
                rgba[byteIndex++] = r1;
                rgba[byteIndex++] = g1;
                rgba[byteIndex++] = b1;
                rgba[byteIndex++] = a1;
            }
        }
        return rgba;
    }
}
