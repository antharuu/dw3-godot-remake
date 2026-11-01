using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// Parses SpriteDrawer sprite sheets (WFIGHTMN, CMFONTCS, STFGTREP) into their part table,
/// frame-id list and frame-offset table.
/// </summary>
/// <remarks>
/// Layout of the sheet body, which starts 4 bytes into the file (the leading u32 is an
/// archive-skip pointer):
/// <code>
/// +0x00 u32 partsOffset     start of the part table, at least 8
/// +0x04 u32 idsOffset       start of the frame-id list, after the part table
/// +0x08 u32[ids] frame offsets, one per frame id, each past the id list terminator
/// parts: 7 x s16 (14 bytes) each, optionally followed by a 2-byte pad
/// ids:   u8 frame ids terminated by 0xFF
/// </code>
/// All offsets are relative to the sheet body.
/// </remarks>
public static class HudSpriteSheetParser
{
    private const int PartBytes = 14;
    private const int FrameOffsetTableStart = 8;

    /// <summary>
    /// Parses a SpriteDrawer sprite sheet into parts, frame-id list and frame-offset table.
    /// Layout (body after 4-byte skip): [parts_offset u32][ids_offset u32][frame_offsets…][parts][ids 0xFF-terminated][frames]
    /// The part table may include a 2-byte pad when the part count is odd.
    /// </summary>
    /// <param name="data">Full file bytes (the first 4 bytes are a skip word).</param>
    public static (IReadOnlyList<HudSpritePart> Parts, IReadOnlyList<int> Ids, IReadOnlyList<int> FrameOffsets)
        Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 12)
        {
            throw new FormatException("Sprite sheet is truncated.");
        }

        // Sheet body starts 4 bytes in (the leading u32 is an archive-skip pointer).
        ReadOnlySpan<byte> sheet = data.AsSpan(4);
        if (sheet.Length < 8)
        {
            throw new FormatException("Sprite sheet body too short for header.");
        }

        int partsOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(sheet);
        int idsOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(sheet.Slice(4));
        if (partsOffset < 8 || idsOffset <= partsOffset || idsOffset > sheet.Length)
        {
            throw new FormatException("Sprite sheet header offsets are invalid.");
        }

        (List<int> ids, int terminator) = ReadFrameIds(sheet, idsOffset);
        HudSpritePart[] parts = ReadParts(sheet, partsOffset, idsOffset);
        int[] frameOffsets = ReadFrameOffsets(sheet, ids.Count, terminator + 1);
        return (parts, ids, frameOffsets);
    }

    /// <summary>Reads the 0xFF-terminated frame-id list starting at <paramref name="idsOffset"/>.</summary>
    /// <param name="sheet">Sheet body.</param>
    /// <param name="idsOffset">Body offset of the first id byte.</param>
    /// <returns>The ids in file order and the body offset of the 0xFF terminator.</returns>
    /// <exception cref="FormatException">The list is empty or runs to the end of the body without a terminator.</exception>
    private static (List<int> Ids, int Terminator) ReadFrameIds(ReadOnlySpan<byte> sheet, int idsOffset)
    {
        var ids = new List<int>();
        int cursor = idsOffset;
        while (cursor < sheet.Length && sheet[cursor] != 0xFF)
        {
            ids.Add(sheet[cursor]);
            cursor++;
        }

        if (ids.Count == 0 || cursor >= sheet.Length)
        {
            throw new FormatException("Sprite sheet has no terminated frame-id list.");
        }

        return (ids, cursor);
    }

    /// <summary>
    /// Reads the part table, which fills [<paramref name="partsOffset"/>, <paramref name="idsOffset"/>)
    /// with 14-byte records and at most one trailing 2-byte pad.
    /// </summary>
    /// <param name="sheet">Sheet body.</param>
    /// <param name="partsOffset">Body offset of the first part record.</param>
    /// <param name="idsOffset">Body offset of the frame-id list, which ends the table.</param>
    /// <returns>The parts in table order.</returns>
    /// <exception cref="FormatException">The table width leaves a remainder other than 0 or 2 bytes.</exception>
    private static HudSpritePart[] ReadParts(ReadOnlySpan<byte> sheet, int partsOffset, int idsOffset)
    {
        int partTableBytes = idsOffset - partsOffset;
        int remainder = partTableBytes % PartBytes;
        if (remainder is not 0 and not 2)
        {
            throw new FormatException("Sprite sheet part table has an unexpected width.");
        }

        int numParts = (partTableBytes - (remainder == 2 ? 2 : 0)) / PartBytes;
        var parts = new HudSpritePart[numParts];
        for (int i = 0; i < numParts; i++)
        {
            ReadOnlySpan<byte> record = sheet.Slice(partsOffset + (i * PartBytes));
            parts[i] = new HudSpritePart(
                BinaryPrimitives.ReadInt16LittleEndian(record),
                BinaryPrimitives.ReadInt16LittleEndian(record.Slice(2)),
                BinaryPrimitives.ReadInt16LittleEndian(record.Slice(4)),
                BinaryPrimitives.ReadInt16LittleEndian(record.Slice(6)),
                BinaryPrimitives.ReadInt16LittleEndian(record.Slice(8)),
                BinaryPrimitives.ReadInt16LittleEndian(record.Slice(10)),
                BinaryPrimitives.ReadInt16LittleEndian(record.Slice(12)));
        }

        return parts;
    }

    /// <summary>Reads one u32 frame offset per frame id from body offset 8.</summary>
    /// <param name="sheet">Sheet body.</param>
    /// <param name="count">Number of frame ids.</param>
    /// <param name="firstFrameOffset">First body offset past the id list terminator; frames start there or later.</param>
    /// <returns>The frame record offsets, in frame-id order.</returns>
    /// <exception cref="FormatException">An offset points into the header or past the end of the body.</exception>
    private static int[] ReadFrameOffsets(ReadOnlySpan<byte> sheet, int count, int firstFrameOffset)
    {
        var frameOffsets = new int[count];
        for (int i = 0; i < count; i++)
        {
            int off = (int)BinaryPrimitives.ReadUInt32LittleEndian(sheet.Slice(FrameOffsetTableStart + (i * 4)));
            if (off < firstFrameOffset || off >= sheet.Length)
            {
                throw new FormatException($"Sprite sheet frame offset {i} (0x{off:X}) is out of range.");
            }

            frameOffsets[i] = off;
        }

        return frameOffsets;
    }
}
