using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// The offset-table archive used by battle model files and by the nested archives inside them.
/// </summary>
/// <remarks>
/// Grammar: the file starts with a table of u32 offsets. The first offset also gives the table
/// size (entry count = first / 4), so it must be a non-zero multiple of four. Each non-zero
/// offset starts an entry that runs to the next non-zero offset, or to the end of the data for
/// the last one. Zero offsets mark absent entries; they keep their index so references by entry
/// number stay valid. Present offsets must be strictly increasing and lie inside the data.
/// </remarks>
public static class ModelArchive
{
    /// <summary>Reads the entry ranges of a top-level archive.</summary>
    /// <param name="data">Raw bytes of the model archive.</param>
    /// <returns>One <see cref="ArchiveRange"/> per entry; absent entries have Start == End == 0.</returns>
    /// <exception cref="FormatException">The offset table is malformed.</exception>
    public static IReadOnlyList<ArchiveRange> ReadBounds(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            throw new FormatException("Model archive is shorter than one offset.");
        }

        uint first = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (first < 4 || first % 4 != 0 || first > data.Length)
        {
            throw new FormatException($"Model archive first offset 0x{first:X} is invalid.");
        }

        int[] offsets = ReadOffsetTable(data, first);
        var bounds = new ArchiveRange[offsets.Length];
        int end = data.Length;
        for (int i = offsets.Length - 1; i >= 0; i--)
        {
            if (offsets[i] == 0)
            {
                bounds[i] = new ArchiveRange(0, 0);
                continue;
            }

            bounds[i] = new ArchiveRange(offsets[i], end);
            end = offsets[i];
        }

        return bounds;
    }

    /// <summary>
    /// Reads the first <paramref name="entryCount"/> offsets of a nested archive spanning
    /// [<paramref name="start"/>, <paramref name="end"/>). Offsets are relative to
    /// <paramref name="start"/>; zero means absent, any other value must lie after the
    /// table and before <paramref name="end"/>.
    /// </summary>
    /// <param name="data">Full model file bytes.</param>
    /// <param name="start">File offset of the nested archive start (= offset table start).</param>
    /// <param name="end">File offset of the end of the nested archive.</param>
    /// <param name="entryCount">Number of offset table slots to read.</param>
    /// <returns>Array of <paramref name="entryCount"/> decoded offsets (relative to <paramref name="start"/>); 0 = absent.</returns>
    /// <exception cref="FormatException">The range or an offset is invalid.</exception>
    public static int[] ReadNestedOffsets(ReadOnlySpan<byte> data, int start, int end, int entryCount)
    {
        if (start < 0 || start >= end || end > data.Length)
        {
            throw new FormatException($"Invalid nested archive bounds 0x{start:X}..0x{end:X}.");
        }

        if (start + (entryCount * 4) > data.Length)
        {
            throw new FormatException($"Nested archive table at 0x{start:X} overruns the data.");
        }

        var offsets = new int[entryCount];
        for (int i = 0; i < entryCount; i++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(start + (i * 4), 4));
            if (value != 0 && (value < entryCount * 4 || value >= end - start))
            {
                throw new FormatException($"Nested archive entry {i} has invalid offset 0x{value:X}.");
            }

            offsets[i] = (int)value;
        }

        return offsets;
    }

    /// <summary>Reads the first / 4 offsets of the table; absent entries stay 0.</summary>
    /// <param name="data">Whole archive.</param>
    /// <param name="first">First offset, a validated non-zero multiple of four that also sizes the table.</param>
    /// <returns>One offset per entry.</returns>
    /// <exception cref="FormatException">A present offset lies inside the table, past the data, or is not increasing.</exception>
    private static int[] ReadOffsetTable(ReadOnlySpan<byte> data, uint first)
    {
        int count = (int)(first / 4);
        var offsets = new int[count];
        int previous = -1;
        for (int i = 0; i < count; i++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i * 4, 4));
            if (value == 0)
            {
                continue;
            }

            if (value < first || value >= data.Length)
            {
                throw new FormatException($"Model archive entry {i} offset 0x{value:X} lies outside the data.");
            }

            if ((int)value <= previous)
            {
                throw new FormatException("Model archive offsets are not increasing.");
            }

            offsets[i] = previous = (int)value;
        }

        return offsets;
    }
}
