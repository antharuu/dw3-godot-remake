using System.Buffers.Binary;
using Dw3.Formats.Field;

namespace Dw3.Formats.Water;

/// <summary>
/// One 18-byte StageTile record: a map object (water, furniture, decoration) drawn from
/// the stage sprite sheet.
/// </summary>
/// <param name="Visible">1 when the object is drawn, 0 when hidden.</param>
/// <param name="Anim">Archive selector; 0xFF selects the shared hidden-spot sprites instead of the stage sheet.</param>
/// <param name="Margin">Culling margin; 0 terminates the table.</param>
/// <param name="Depth">Draw layer.</param>
/// <param name="Frame">Sprite-sheet frame id when the object does not cycle frames.</param>
/// <param name="Cycle">0 static, 1 frame cycle, 2 and 3 palette-row cycles.</param>
/// <param name="CycleFirst">First frame id or palette row of the cycle.</param>
/// <param name="CycleLast">Last frame id or palette row of the cycle (inclusive).</param>
/// <param name="CycleDelay">Frames between two cycle steps.</param>
/// <param name="ClutRow">Palette row offset when the object does not cycle palettes.</param>
/// <param name="X">Map X position.</param>
/// <param name="Y">Map Y position.</param>
/// <param name="SortY">Y coordinate used for depth sorting.</param>
/// <param name="CycleTime">Initial cycle timer.</param>
/// <param name="SourceAddress">RAM address of this record inside the overlay.</param>
public sealed record StageTile(
    byte Visible, byte Anim, byte Margin, byte Depth, byte Frame, byte Cycle, byte CycleFirst, byte CycleLast,
    byte CycleDelay, byte ClutRow, short X, short Y, short SortY, short CycleTime, uint SourceAddress);

/// <summary>Decodes the margin-terminated StageTile table of a WSTAG overlay.</summary>
public static class StageTileDecoder
{
    /// <summary>Size in bytes of one StageTile record.</summary>
    public const int RecordSize = 18;

    // The executable reserves room for at most this many records per stage.
    private const int MaximumRecords = 256;

    /// <summary>Decodes records starting at RAM <paramref name="address"/> up to the first zero margin.</summary>
    /// <exception cref="FormatException">The table leaves the overlay, starts with the terminator, has no terminator, or holds an unsupported record.</exception>
    public static IReadOnlyList<StageTile> Decode(ReadOnlySpan<byte> overlay, uint address)
    {
        var tiles = new List<StageTile>();
        for (int index = 0; index < MaximumRecords; index++)
        {
            uint recordAddress = address + (uint)(index * RecordSize);
            ReadOnlySpan<byte> raw = Read(overlay, recordAddress);
            if (raw[2] == 0)
            {
                if (index == 0)
                    throw new FormatException("StageTile table begins with terminator");
                return tiles;
            }
            if (raw[0] > 1 || raw[5] > 3 || raw[6] > raw[7])
                throw new FormatException($"unsupported StageTile record index={index}");
            tiles.Add(new StageTile(
                raw[0], raw[1], raw[2], raw[3], raw[4], raw[5], raw[6], raw[7], raw[8], raw[9],
                BinaryPrimitives.ReadInt16LittleEndian(raw[10..]),
                BinaryPrimitives.ReadInt16LittleEndian(raw[12..]),
                BinaryPrimitives.ReadInt16LittleEndian(raw[14..]),
                BinaryPrimitives.ReadInt16LittleEndian(raw[16..]),
                recordAddress));
        }
        throw new FormatException("StageTile table has no terminator");
    }

    private static ReadOnlySpan<byte> Read(ReadOnlySpan<byte> overlay, uint address)
    {
        long offset = (long)address - FieldOverlayLayout.LoadAddress;
        if (offset < 0 || offset + RecordSize > overlay.Length)
            throw new FormatException($"StageTile range outside overlay address=0x{address:X8}");
        return overlay.Slice((int)offset, RecordSize);
    }
}
