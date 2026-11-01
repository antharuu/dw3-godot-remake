using System.Buffers.Binary;

namespace Dw3.Formats.Water;

/// <summary>
/// Decodes the stage sprite sheet stored as one entry of the stage TMPK archive
/// (<c>DAT/FIELD/STAGE/SnnnTMPK.BIN</c>).
///
/// Sheet layout (little-endian): u32 part-table offset, u32 frame-id-table offset,
/// then one u32 frame offset per frame id starting at byte 8. The part table holds
/// 14-byte records (seven s16) and may be followed by a 2-byte alignment pad. The
/// frame-id table is a byte list terminated by 0xFF. Each frame starts at its offset
/// and runs to the end of the sheet.
/// </summary>
public static class SpriteSheetDecoder
{
    private const int PartSize = 14;

    /// <summary>
    /// Returns archive entry <paramref name="entryIndex"/> of a TMPK archive, bounded by its
    /// own offset and the next one. Reading only those two offsets tolerates archives whose
    /// table carries an empty entry between other entries.
    /// </summary>
    /// <param name="archive">Full TMPK archive whose leading words form the entry offset table.</param>
    /// <param name="entryIndex">Zero-based member whose bounded slice is requested.</param>
    /// <returns>The member bytes between this entry's offset and the next offset-table value.</returns>
    public static ReadOnlyMemory<byte> ReadArchiveEntry(ReadOnlyMemory<byte> archive, int entryIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryIndex);
        ReadOnlySpan<byte> span = archive.Span;
        if (span.Length < ((entryIndex + 2) * 4))
        {
            throw new FormatException("sprite sheet archive offset table truncated");
        }

        uint start = BinaryPrimitives.ReadUInt32LittleEndian(span[(entryIndex * 4)..]);
        uint end = BinaryPrimitives.ReadUInt32LittleEndian(span[((entryIndex + 1) * 4)..]);
        if (start < ((entryIndex + 1) * 4) || end <= start || end > span.Length)
        {
            throw new FormatException("sprite sheet archive offsets invalid");
        }

        return archive[(int)start..(int)end];
    }

    /// <summary>Decodes the parts and frames of one sheet entry.</summary>
    /// <param name="entry">Bounded TMPK member beginning with the sheet header at byte zero.</param>
    /// <returns>Parts and frame bodies keyed by the sheet's terminated frame-id table.</returns>
    /// <exception cref="FormatException">The sheet header, tables, or offsets are inconsistent.</exception>
    public static SpriteSheet Decode(ReadOnlyMemory<byte> entry)
    {
        ReadOnlySpan<byte> span = entry.Span;
        if (span.Length < 12)
        {
            throw new FormatException("sprite sheet truncated");
        }

        uint partsOffset = BinaryPrimitives.ReadUInt32LittleEndian(span);
        uint idsOffset = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        if (partsOffset < 8 || partsOffset >= idsOffset || idsOffset >= span.Length)
        {
            throw new FormatException("sprite sheet offsets invalid");
        }

        List<int> ids = ReadFrameIds(span, idsOffset);
        uint[] offsets = ReadFrameOffsets(span, ids, idsOffset);
        SpritePart[] parts = ReadParts(span, partsOffset, idsOffset);
        return new SpriteSheet(parts, ReadFrames(entry, ids, offsets));
    }

    /// <summary>Reads the byte-sized frame identifiers up to the source table's terminator byte.</summary>
    /// <param name="sheet">Complete bounded sheet entry.</param>
    /// <param name="idsOffset">Byte offset of the terminated identifier table.</param>
    /// <returns>Frame identifiers in the same order as their offset words.</returns>
    /// <exception cref="FormatException">No terminator follows at least one frame identifier.</exception>
    private static List<int> ReadFrameIds(ReadOnlySpan<byte> sheet, uint idsOffset)
    {
        var ids = new List<int>();
        int cursor = (int)idsOffset;
        while (cursor < sheet.Length && sheet[cursor] is not 0xFF and not 0x88)
        {
            ids.Add(sheet[cursor++]);
        }

        if (ids.Count == 0 || cursor >= sheet.Length)
        {
            throw new FormatException("sprite sheet has no frame terminator");
        }

        return ids;
    }

    /// <summary>Reads and bounds-checks one four-byte frame offset for every identifier.</summary>
    /// <param name="sheet">Complete bounded sheet entry.</param>
    /// <param name="ids">Frame identifiers which determine the offset-word count.</param>
    /// <param name="idsOffset">Lower bound after the frame-offset table.</param>
    /// <returns>Validated offsets into <paramref name="sheet"/>.</returns>
    /// <exception cref="FormatException">An offset word is absent or points outside the frame area.</exception>
    private static uint[] ReadFrameOffsets(ReadOnlySpan<byte> sheet, List<int> ids, uint idsOffset)
    {
        var offsets = new uint[ids.Count];
        for (int index = 0; index < ids.Count; index++)
        {
            if (8 + (4 * (index + 1)) > sheet.Length)
            {
                throw new FormatException("sprite sheet frame offset invalid");
            }

            offsets[index] = BinaryPrimitives.ReadUInt32LittleEndian(sheet[(8 + (4 * index))..]);
            if (offsets[index] < idsOffset || offsets[index] >= sheet.Length)
            {
                throw new FormatException("sprite sheet frame offset invalid");
            }
        }

        return offsets;
    }

    /// <summary>Decodes the fixed seven-s16 part table that precedes the identifier bytes.</summary>
    /// <param name="sheet">Complete bounded sheet entry.</param>
    /// <param name="partsOffset">First byte of the part records.</param>
    /// <param name="idsOffset">First byte after the part records and optional alignment pad.</param>
    /// <returns>One texture part for every complete 14-byte source record.</returns>
    /// <exception cref="FormatException">The alignment gap is neither zero nor two bytes.</exception>
    private static SpritePart[] ReadParts(ReadOnlySpan<byte> sheet, uint partsOffset, uint idsOffset)
    {
        uint partBytes = idsOffset - partsOffset;
        if (partBytes % PartSize is not 0 and not 2)
        {
            throw new FormatException("sprite part table width invalid");
        }

        var parts = new SpritePart[partBytes / PartSize];
        for (int index = 0; index < parts.Length; index++)
        {
            ReadOnlySpan<byte> raw = sheet.Slice((int)partsOffset + (PartSize * index), PartSize);
            parts[index] = new SpritePart(S16(raw, 0), S16(raw, 2), S16(raw, 4), S16(raw, 6), S16(raw, 8), S16(raw, 10), S16(raw, 12));
        }

        return parts;
    }

    /// <summary>Associates each frame identifier with its source tail, retaining the final duplicate identifier.</summary>
    /// <param name="entry">Bounded sheet entry that owns all frame bytes.</param>
    /// <param name="ids">Frame identifiers in offset-table order.</param>
    /// <param name="offsets">Validated source offsets paired with <paramref name="ids"/>.</param>
    /// <returns>Frame bodies indexed by identifier.</returns>
    private static Dictionary<int, ReadOnlyMemory<byte>> ReadFrames(ReadOnlyMemory<byte> entry, List<int> ids, uint[] offsets)
    {
        var frames = new Dictionary<int, ReadOnlyMemory<byte>>();
        for (int index = 0; index < ids.Count; index++)
        {
            frames[ids[index]] = entry[(int)offsets[index]..];
        }

        return frames;
    }

    /// <summary>Reads one signed 16-bit little-endian part-table field.</summary>
    /// <param name="raw">The complete fixed-size source part record.</param>
    /// <param name="offset">Byte offset of the two-byte signed field inside <paramref name="raw"/>.</param>
    /// <returns>The decoded signed source field.</returns>
    private static short S16(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadInt16LittleEndian(raw[offset..]);
}
