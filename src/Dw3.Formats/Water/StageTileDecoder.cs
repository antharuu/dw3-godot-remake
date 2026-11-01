using System.Buffers.Binary;
using Dw3.Formats.Field;

namespace Dw3.Formats.Water;

/// <summary>Decodes the margin-terminated StageTile table of a WSTAG overlay.</summary>
public static class StageTileDecoder
{
    /// <summary>Size in bytes of one StageTile record.</summary>
    public const int RecordSize = 18;

    // The executable reserves room for at most this many records per stage.
    private const int MaximumRecords = 256;

    /// <summary>Decodes records starting at RAM <paramref name="address"/> up to the first zero margin.</summary>
    /// <param name="overlay">The WSTAG overlay bytes loaded at the field overlay base address.</param>
    /// <param name="address">The RAM address of the first StageTile record.</param>
    /// <returns>The non-terminator StageTile records in source order.</returns>
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
                EnsureNotFirstRecord(index);
                return tiles;
            }

            ValidateRecord(raw, index);
            tiles.Add(DecodeRecord(raw, recordAddress));
        }

        throw new FormatException("StageTile table has no terminator");
    }

    /// <summary>Rejects a table that begins with its zero-record terminator.</summary>
    /// <param name="index">The current record index.</param>
    /// <exception cref="FormatException">The first record is the terminator.</exception>
    private static void EnsureNotFirstRecord(int index)
    {
        if (index == 0)
        {
            throw new FormatException("StageTile table begins with terminator");
        }
    }

    /// <summary>Checks whether a non-terminator raw record uses supported field values.</summary>
    /// <param name="raw">The 18-byte source record.</param>
    /// <param name="index">The source table index used in the error.</param>
    /// <exception cref="FormatException">The record contains unsupported selector values.</exception>
    private static void ValidateRecord(ReadOnlySpan<byte> raw, int index)
    {
        if (raw[0] > 1 || raw[5] > 3 || raw[6] > raw[7])
        {
            throw new FormatException($"unsupported StageTile record index={index}");
        }
    }

    /// <summary>Decodes the fixed-layout raw bytes into a StageTile value.</summary>
    /// <param name="raw">The 18-byte source record.</param>
    /// <param name="recordAddress">The RAM address from which the record was read.</param>
    /// <returns>The decoded tile record.</returns>
    private static StageTile DecodeRecord(ReadOnlySpan<byte> raw, uint recordAddress) => new(
        raw[0],
        raw[1],
        raw[2],
        raw[3],
        raw[4],
        raw[5],
        raw[6],
        raw[7],
        raw[8],
        raw[9],
        BinaryPrimitives.ReadInt16LittleEndian(raw[10..]),
        BinaryPrimitives.ReadInt16LittleEndian(raw[12..]),
        BinaryPrimitives.ReadInt16LittleEndian(raw[14..]),
        BinaryPrimitives.ReadInt16LittleEndian(raw[16..]),
        recordAddress);

    /// <summary>Returns exactly one StageTile record at its RAM address.</summary>
    /// <param name="overlay">The WSTAG overlay bytes loaded at the field overlay base address.</param>
    /// <param name="address">The RAM address of the requested record.</param>
    /// <returns>The fixed-size raw record bytes.</returns>
    /// <exception cref="FormatException">The requested record range lies outside the overlay.</exception>
    private static ReadOnlySpan<byte> Read(ReadOnlySpan<byte> overlay, uint address)
    {
        long offset = (long)address - FieldOverlayLayout.LoadAddress;
        if (offset < 0 || offset + RecordSize > overlay.Length)
        {
            throw new FormatException($"StageTile range outside overlay address=0x{address:X8}");
        }

        return overlay.Slice((int)offset, RecordSize);
    }
}
