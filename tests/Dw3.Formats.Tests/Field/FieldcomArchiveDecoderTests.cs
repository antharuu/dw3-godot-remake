using System.Buffers.Binary;
using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Field;

public sealed class FieldcomArchiveDecoderTests
{
    [Fact]
    public void ReadsFourBoundedEntriesFromSyntheticArchive()
    {
        byte[] archive = new byte[32];
        int[] offsets = [16, 20, 24, 28];
        for (int index = 0; index < offsets.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(index * 4, 4), (uint)offsets[index]);
        }

        archive[16] = 0xA1;
        archive[20] = 0xB2;
        archive[24] = 0xC3;
        archive[28] = 0xD4;

        FieldcomArchive decoded = FieldcomArchiveDecoder.Decode(archive);

        Assert.Equal(4, decoded.Entries.Count);
        Assert.Equal(0xA1, decoded.Entries[0].Span[0]);
        Assert.Equal(0xB2, decoded.Entries[1].Span[0]);
        Assert.Equal(0xC3, decoded.Entries[2].Span[0]);
        Assert.Equal(0xD4, decoded.Entries[3].Span[0]);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    public void RejectsTruncatedOrInvalidOffsetTable(int size)
    {
        byte[] archive = new byte[size];
        if (size == 16)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(archive, 16);
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), 16);
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(8), 16);
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(12), 16);
        }

        FormatException error = Assert.Throws<FormatException>(() => FieldcomArchiveDecoder.Decode(archive));

        Assert.Contains("FIELDCOM", error.Message, StringComparison.Ordinal);
    }
}
