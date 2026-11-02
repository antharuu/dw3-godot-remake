using System.Buffers.Binary;
using Dw3.Formats.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// Decodes the 4-bit texture pages that back the battle HUD (file 0x456), the result screen
/// (file 0x7A0) and the menu sprites (file 0x287).
/// </summary>
/// <remarks>
/// Each page is a 4-bit TIM: magic 0x10 at +0, flags at +4 (bit 3 = CLUT block present), an
/// optional CLUT block whose first u32 is its byte size, then the image block: u32 byte size,
/// u16 x, u16 y, u16 width in halfwords (64), u16 height (256) and the pixel halfwords.
/// </remarks>
public static class HudTexturePageDecoder
{
    /// <summary>
    /// Decodes FILE_BATTLE_IMAGES (file 0x456) entry 1 into one 4-bit texture page.
    /// Entry 0 is the cursor bar; entry 1 is an inner archive containing one RLEN TIM.
    /// </summary>
    /// <param name="imagesData">Raw bytes of FILE_BATTLE_IMAGES (file 0x456).</param>
    /// <returns>Single-element list containing the decoded 4-bit HUD texture page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="imagesData"/> is null.</exception>
    /// <exception cref="FormatException">The archive structure or the TIM is malformed.</exception>
    public static IReadOnlyList<HudTexturePage> BuildHudTexturePages(byte[] imagesData)
    {
        ArgumentNullException.ThrowIfNull(imagesData);
        if (imagesData.Length < 8)
        {
            throw new FormatException("FILE_BATTLE_IMAGES is too short for two archive entries.");
        }

        // Outer archive: u32[0]=entry0_offset, u32[1]=entry1_offset.
        int entry1Off = (int)BinaryPrimitives.ReadUInt32LittleEndian(imagesData.AsSpan(4));
        if (entry1Off < 8 || entry1Off >= imagesData.Length)
        {
            throw new FormatException("FILE_BATTLE_IMAGES entry 1 offset is out of range.");
        }

        // Entry 1 is itself a one-entry inner archive.
        ReadOnlySpan<byte> inner = imagesData.AsSpan(entry1Off);
        if (inner.Length < 4)
        {
            throw new FormatException("FILE_BATTLE_IMAGES inner archive is too short.");
        }

        int texOff = (int)BinaryPrimitives.ReadUInt32LittleEndian(inner);
        if (texOff < 4 || texOff >= inner.Length)
        {
            throw new FormatException("FILE_BATTLE_IMAGES inner texture offset is out of range.");
        }

        // RLEN-compressed TIM.
        byte[] raw = HudRlenDecoder.Decompress(inner.Slice(texOff));
        return [DecodeFourBitTim(raw)];
    }

    /// <summary>
    /// Decodes a multi-entry model-archive texture (used by the report texture file 0x7A0).
    /// The archive uses the same ModelArchive offset-table format as fighter model files.
    /// The input starts with a 4-byte pointer that must equal 4 (one-entry outer wrapper).
    /// </summary>
    /// <param name="archiveWithSkip">Raw bytes of the report texture archive (file 0x7A0), starting with the 4-byte skip pointer.</param>
    /// <returns>List of decoded 4-bit texture pages in archive order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="archiveWithSkip"/> is null.</exception>
    /// <exception cref="FormatException">The archive is too short, the skip pointer is wrong, or no texture pages are present.</exception>
    public static IReadOnlyList<HudTexturePage> BuildReportTexturePages(byte[] archiveWithSkip)
    {
        ArgumentNullException.ThrowIfNull(archiveWithSkip);
        if (archiveWithSkip.Length < 12)
        {
            throw new FormatException("Report texture file is too short.");
        }

        uint skip = BinaryPrimitives.ReadUInt32LittleEndian(archiveWithSkip.AsSpan());
        if (skip != 4)
        {
            throw new FormatException($"Report texture file outer pointer is {skip}, expected 4.");
        }

        // Strip the leading 4-byte skip and parse the inner archive.
        byte[] archive = archiveWithSkip[4..];
        IReadOnlyList<ArchiveRange> bounds = ModelArchive.ReadBounds(archive.AsSpan());
        var pages = new List<HudTexturePage>();
        foreach (ArchiveRange range in bounds.Where(range => !range.IsEmpty))
        {
            byte[] entry = archive[range.Start..range.End];
            byte[] raw = RlenDecoder.IsRlen(entry) ? RlenDecoder.Decompress(entry) : entry;
            pages.Add(DecodeFourBitTim(raw));
        }

        if (pages.Count == 0)
        {
            throw new FormatException("Report texture archive contains no texture pages.");
        }

        return pages;
    }

    /// <summary>
    /// Decodes the CMFONTTM.BIN texture archive (file 0x287) used by the CMFONTCS menu sheet.
    /// Format: 4-byte skip, then an offset table (u32 values relative to byte 4, terminated by 0),
    /// each pointing to an RLEN-compressed 4-bit TIM page.
    /// </summary>
    /// <param name="data">Raw bytes of the CMFONTTM.BIN menu texture archive (file 0x287).</param>
    /// <returns>List of decoded 4-bit texture pages in archive entry order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="FormatException">The archive is truncated or a TIM entry is malformed.</exception>
    public static IReadOnlyList<HudTexturePage> BuildMenuTexturePages(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 16)
        {
            throw new FormatException("Menu texture archive is truncated.");
        }

        var pages = new List<HudTexturePage>();
        foreach (int offset in ReadMenuOffsets(data))
        {
            byte[] raw = HudRlenDecoder.Decompress(data.AsSpan(4 + offset));
            pages.Add(DecodeFourBitTim(raw));
        }

        return pages;
    }

    /// <summary>
    /// Decodes an RLEN-compressed or raw 4-bit TIM (with or without CLUT block) into a texture page.
    /// The image block must be 64 halfwords × 256 rows.
    /// </summary>
    /// <param name="raw">Decompressed or raw TIM bytes starting with magic 0x10.</param>
    /// <returns>The decoded 256-row × 64-halfword texture page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="raw"/> is null.</exception>
    /// <exception cref="FormatException">The TIM magic is wrong, the image block is truncated, or the dimensions do not match the expected page size.</exception>
    public static HudTexturePage DecodeFourBitTim(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(raw) != 0x10)
        {
            throw new FormatException("4-bit TIM magic 0x10 not found.");
        }

        int cursor = ImageBlockOffset(raw);
        if (cursor + 12 > raw.Length)
        {
            throw new FormatException("TIM image block header is truncated.");
        }

        ushort words = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(cursor + 8));
        ushort height = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(cursor + 10));
        if (words != HudTexturePage.PageWidthWords || height != HudTexturePage.PageHeight)
        {
            throw new FormatException($"TIM image block is {words}×{height} halfwords, expected {HudTexturePage.PageWidthWords}×{HudTexturePage.PageHeight}.");
        }

        uint imgSize = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(cursor));
        if (imgSize < 12 || cursor + imgSize > raw.Length)
        {
            throw new FormatException("TIM image block is truncated.");
        }

        return new HudTexturePage(ReadRows(raw, cursor + 12, words, height));
    }

    /// <summary>
    /// Reads the menu archive offset table at byte 4: u32 offsets relative to byte 4, terminated by 0.
    /// </summary>
    /// <param name="data">Whole menu texture file.</param>
    /// <returns>The non-zero offsets in table order; at least one.</returns>
    /// <exception cref="FormatException">The table is truncated, empty, or an offset points into the table or past the file.</exception>
    private static List<int> ReadMenuOffsets(byte[] data)
    {
        var offsets = new List<int>();
        for (int index = 0; ; index++)
        {
            int at = 4 + (index * 4);
            if (at + 4 > data.Length)
            {
                throw new FormatException("Menu texture archive offset table is truncated.");
            }

            int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
            if (offset == 0)
            {
                break;
            }

            if (offset < index * 4 || 4 + offset >= data.Length)
            {
                throw new FormatException($"Menu texture archive offset {index} (0x{offset:X}) is invalid.");
            }

            offsets.Add(offset);
        }

        if (offsets.Count == 0)
        {
            throw new FormatException("Menu texture archive has no entries.");
        }

        return offsets;
    }

    /// <summary>Returns the offset of the TIM image block, skipping the CLUT block when flag bit 3 is set.</summary>
    /// <param name="raw">Decompressed TIM starting with the 0x10 magic.</param>
    /// <returns>Byte offset of the image block header (8 without CLUT, 8 + CLUT size otherwise).</returns>
    /// <exception cref="FormatException">The CLUT block header is truncated or its size is below 12 or past the data.</exception>
    private static int ImageBlockOffset(byte[] raw)
    {
        int cursor = 8;
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(4));
        if ((flags & 8) == 0)
        {
            return cursor;
        }

        // Skip the CLUT block (present in 4/8-bit TIMs).
        if (cursor + 4 > raw.Length)
        {
            throw new FormatException("TIM CLUT block header is truncated.");
        }

        uint clutSize = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(cursor));
        if (clutSize < 12 || cursor + clutSize > raw.Length)
        {
            throw new FormatException("TIM CLUT block is invalid.");
        }

        return cursor + (int)clutSize;
    }

    /// <summary>Reads <paramref name="height"/> rows of <paramref name="words"/> little-endian halfwords.</summary>
    /// <param name="raw">Decompressed TIM.</param>
    /// <param name="dataStart">Offset of the first pixel halfword (image block header + 12).</param>
    /// <param name="words">Halfwords per row.</param>
    /// <param name="height">Row count.</param>
    /// <returns>Rows in top-to-bottom order.</returns>
    private static IReadOnlyList<ushort>[] ReadRows(byte[] raw, int dataStart, int words, int height)
    {
        var rows = new IReadOnlyList<ushort>[height];
        for (int row = 0; row < height; row++)
        {
            var rowData = new ushort[words];
            for (int w = 0; w < words; w++)
            {
                rowData[w] = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(dataStart + (((row * words) + w) * 2)));
            }

            rows[row] = rowData;
        }

        return rows;
    }
}
