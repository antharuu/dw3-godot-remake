using System.Buffers.Binary;

namespace Dw3.Formats.Text;

/// <summary>
/// Reads and validates the count and offset table that precedes field-text entries.
/// </summary>
internal static class TextTableHeaderReader
{
    private const int CountSize = sizeof(uint);
    private const int OffsetSize = sizeof(uint);
    private const uint MaximumEntryCount = 0x10000;

    /// <summary>
    /// Reads the string offsets and verifies that every entry begins inside the payload.
    /// </summary>
    /// <param name="data">Complete FSTALK file contents.</param>
    /// <returns>Validated offsets in table order.</returns>
    /// <exception cref="FormatException">The count or one offset cannot describe a valid payload.</exception>
    public static int[] ReadOffsets(ReadOnlySpan<byte> data)
    {
        EnsureCountIsAvailable(data);
        int count = ReadCount(data);
        int headerSize = GetHeaderSize(count);
        EnsureHeaderFits(data, headerSize);
        int[] offsets = ReadOffsets(data, count, headerSize);
        EnsureOffsetsAreMonotonic(offsets);
        return offsets;
    }

    /// <summary>Ensures that the entry count field is available.</summary>
    /// <param name="data">Complete FSTALK file contents.</param>
    private static void EnsureCountIsAvailable(ReadOnlySpan<byte> data)
    {
        if (data.Length < CountSize)
        {
            throw new FormatException("Text table too short to read entry count.");
        }
    }

    /// <summary>Reads the bounded entry count.</summary>
    /// <param name="data">Complete FSTALK file contents.</param>
    /// <returns>The validated number of text entries.</returns>
    private static int ReadCount(ReadOnlySpan<byte> data)
    {
        uint rawCount = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (rawCount > MaximumEntryCount)
        {
            throw new FormatException($"Text table entry count {rawCount} is implausibly large.");
        }

        return (int)rawCount;
    }

    /// <summary>Calculates the complete header size.</summary>
    /// <param name="count">Number of offset records.</param>
    /// <returns>The header length in bytes.</returns>
    private static int GetHeaderSize(int count) => CountSize + (count * OffsetSize);

    /// <summary>Ensures that the header fits in the source file.</summary>
    /// <param name="data">Complete FSTALK file contents.</param>
    /// <param name="headerSize">Required header size.</param>
    private static void EnsureHeaderFits(ReadOnlySpan<byte> data, int headerSize)
    {
        if (headerSize > data.Length)
        {
            throw new FormatException($"Text table offset header is truncated (needs {headerSize} bytes, got {data.Length}).");
        }
    }

    /// <summary>Reads and validates every offset record.</summary>
    /// <param name="data">Complete FSTALK file contents.</param>
    /// <param name="count">Number of offset records.</param>
    /// <param name="headerSize">First valid payload position.</param>
    /// <returns>Offsets in table order.</returns>
    private static int[] ReadOffsets(ReadOnlySpan<byte> data, int count, int headerSize)
    {
        var offsets = new int[count];
        for (int index = 0; index < count; index++)
        {
            uint offset = ReadOffset(data, index);
            EnsureOffsetIsInPayload(offset, headerSize, data.Length, index);
            offsets[index] = (int)offset;
        }

        return offsets;
    }

    /// <summary>Reads one little-endian offset from the table.</summary>
    /// <param name="data">Complete FSTALK file contents.</param>
    /// <param name="index">Zero-based offset-table index.</param>
    /// <returns>The unvalidated unsigned file offset.</returns>
    private static uint ReadOffset(ReadOnlySpan<byte> data, int index) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data[(CountSize + (index * OffsetSize))..]);

    /// <summary>Ensures that an offset points inside the string payload.</summary>
    /// <param name="offset">Offset read from the table.</param>
    /// <param name="headerSize">First valid payload position.</param>
    /// <param name="dataLength">Complete file length.</param>
    /// <param name="index">Zero-based offset-table index.</param>
    private static void EnsureOffsetIsInPayload(uint offset, int headerSize, int dataLength, int index)
    {
        if (offset < (uint)headerSize || offset >= (uint)dataLength)
        {
            throw new FormatException($"Text table entry {index} offset 0x{offset:X} is outside the string payload.");
        }
    }

    /// <summary>Ensures that entries retain their source-table order.</summary>
    /// <param name="offsets">Validated offsets in table order.</param>
    private static void EnsureOffsetsAreMonotonic(int[] offsets)
    {
        for (int index = 1; index < offsets.Length; index++)
        {
            if (offsets[index] < offsets[index - 1])
            {
                throw new FormatException($"Text table offsets are not monotonic at index {index}.");
            }
        }
    }
}
