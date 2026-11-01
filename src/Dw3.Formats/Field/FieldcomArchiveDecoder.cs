using System.Buffers.Binary;

namespace Dw3.Formats.Field;

/// <summary>
/// Reads the fixed four-entry FIELDCOM archive. Its u32 offset table occupies the
/// first 16 bytes, and each entry runs up to the next offset or archive end.
/// </summary>
public static class FieldcomArchiveDecoder
{
    private const int EntryCount = 4;
    private const int HeaderBytes = EntryCount * sizeof(uint);

    /// <exception cref="FormatException">The fixed archive header or an entry boundary is invalid.</exception>
    public static FieldcomArchive Decode(ReadOnlyMemory<byte> archive)
    {
        ReadOnlySpan<byte> span = archive.Span;
        if (span.Length < HeaderBytes)
        {
            throw new FormatException("FIELDCOM archive header is truncated");
        }

        var offsets = new int[EntryCount];
        for (int index = 0; index < EntryCount; index++)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(span[(index * sizeof(uint))..]);
            if (offset > int.MaxValue)
            {
                throw new FormatException("FIELDCOM archive offset exceeds supported range");
            }

            offsets[index] = (int)offset;
            if (offsets[index] < HeaderBytes || offsets[index] >= span.Length ||
                (index > 0 && offsets[index] <= offsets[index - 1]))
            {
                throw new FormatException("FIELDCOM archive offsets are invalid");
            }
        }

        var entries = new ReadOnlyMemory<byte>[EntryCount];
        for (int index = 0; index < EntryCount; index++)
        {
            int start = offsets[index];
            int end = index + 1 < EntryCount ? offsets[index + 1] : span.Length;
            entries[index] = archive[start..end];
        }

        return new FieldcomArchive(entries);
    }
}
