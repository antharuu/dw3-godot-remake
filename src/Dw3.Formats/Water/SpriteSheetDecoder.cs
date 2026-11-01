using System.Buffers.Binary;

namespace Dw3.Formats.Water;

/// <summary>
/// One sprite-sheet part: a textured rectangle in the object VRAM.
/// </summary>
/// <param name="U">Texel column; the page index is <c>U / 256</c> for 4-bit and <c>U / 128</c> for 8-bit parts.</param>
/// <param name="V">Texel row inside the page.</param>
/// <param name="Width">Width in texels.</param>
/// <param name="Height">Height in texels.</param>
/// <param name="ClutX">Palette column (4-bit: relative to VRAM x 0x140, 8-bit: inside the alternate palette row).</param>
/// <param name="ClutY">Palette row (4-bit: relative to VRAM y 0x100, 8-bit: absolute VRAM row).</param>
/// <param name="Mode">0 for 4-bit texels, 1 for 8-bit texels.</param>
public sealed record SpritePart(short U, short V, short Width, short Height, short ClutX, short ClutY, short Mode);

/// <summary>A decoded sprite sheet: its parts and the raw bytes of each frame, keyed by frame id.</summary>
public sealed record SpriteSheet(IReadOnlyList<SpritePart> Parts, IReadOnlyDictionary<int, ReadOnlyMemory<byte>> Frames);

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
    public static ReadOnlyMemory<byte> ReadArchiveEntry(ReadOnlyMemory<byte> archive, int entryIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryIndex);
        ReadOnlySpan<byte> span = archive.Span;
        if (span.Length < (entryIndex + 2) * 4)
            throw new FormatException("sprite sheet archive offset table truncated");
        uint start = BinaryPrimitives.ReadUInt32LittleEndian(span[(entryIndex * 4)..]);
        uint end = BinaryPrimitives.ReadUInt32LittleEndian(span[((entryIndex + 1) * 4)..]);
        if (start < (entryIndex + 1) * 4 || end <= start || end > span.Length)
            throw new FormatException("sprite sheet archive offsets invalid");
        return archive[(int)start..(int)end];
    }

    /// <summary>Decodes the parts and frames of one sheet entry.</summary>
    /// <exception cref="FormatException">The sheet header, tables, or offsets are inconsistent.</exception>
    public static SpriteSheet Decode(ReadOnlyMemory<byte> entry)
    {
        ReadOnlySpan<byte> span = entry.Span;
        if (span.Length < 12)
            throw new FormatException("sprite sheet truncated");
        uint partsOffset = BinaryPrimitives.ReadUInt32LittleEndian(span);
        uint idsOffset = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        if (partsOffset < 8 || partsOffset >= idsOffset || idsOffset >= span.Length)
        {
            throw new FormatException("sprite sheet offsets invalid");
        }

        var ids = new List<int>();
        int cursor = (int)idsOffset;
        while (cursor < span.Length && span[cursor] is not 0xFF and not 0x88)
            ids.Add(span[cursor++]);
        if (ids.Count == 0 || cursor >= span.Length)
        {
            throw new FormatException("sprite sheet has no frame terminator");
        }

        var offsets = new uint[ids.Count];
        for (int index = 0; index < ids.Count; index++)
        {
            if (8 + 4 * (index + 1) > span.Length)
                throw new FormatException("sprite sheet frame offset invalid");
            offsets[index] = BinaryPrimitives.ReadUInt32LittleEndian(span[(8 + 4 * index)..]);
            if (offsets[index] < idsOffset || offsets[index] >= span.Length)
                throw new FormatException("sprite sheet frame offset invalid");
        }

        // The id table is 4-byte aligned, so an odd part count leaves a 2-byte pad.
        uint partBytes = idsOffset - partsOffset;
        if (partBytes % PartSize is not 0 and not 2)
            throw new FormatException("sprite part table width invalid");
        var parts = new SpritePart[partBytes / PartSize];
        for (int index = 0; index < parts.Length; index++)
        {
            ReadOnlySpan<byte> raw = span.Slice((int)partsOffset + PartSize * index, PartSize);
            parts[index] = new SpritePart(S16(raw, 0), S16(raw, 2), S16(raw, 4), S16(raw, 6), S16(raw, 8), S16(raw, 10), S16(raw, 12));
        }

        // A repeated frame id keeps its last offset.
        var frames = new Dictionary<int, ReadOnlyMemory<byte>>();
        for (int index = 0; index < ids.Count; index++)
            frames[ids[index]] = entry[(int)offsets[index]..];
        return new SpriteSheet(parts, frames);
    }

    private static short S16(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadInt16LittleEndian(raw[offset..]);
}
