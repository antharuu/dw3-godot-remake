using System.Buffers.Binary;
using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Field;

public sealed class TmpkMapDecoderTests
{
    [Fact]
    public void DecodesSixLevelMapIntoCells()
    {
        byte[] archive = BuildArchive();

        TmpkMap map = TmpkMapDecoder.DecodeEntry(archive, 0);

        Assert.Equal(128, map.Width);
        Assert.Equal(128, map.Height);
        Assert.All(map.Cells, cell => Assert.Equal((byte)0xA5, cell));
    }

    [Fact]
    public void RejectsTruncatedHierarchy()
    {
        byte[] archive = BuildArchive();
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4, 4), 4);

        FormatException error = Assert.Throws<FormatException>(() => TmpkMapDecoder.DecodeEntry(archive, 0));

        Assert.Contains("component", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] BuildArchive()
    {
        const int entryOffset = 4;
        const int gridOffset = 24;
        const int cells64Offset = 27;
        const int cells32Offset = 31;
        const int cells16Offset = 39;
        const int cells8Offset = 47;
        const int pixelsOffset = 55;
        var archive = new byte[entryOffset + pixelsOffset + 64];
        BinaryPrimitives.WriteUInt32LittleEndian(archive, entryOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(entryOffset), gridOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(entryOffset + 4), cells64Offset);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(entryOffset + 8), cells32Offset);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(entryOffset + 12), cells16Offset);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(entryOffset + 16), cells8Offset);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(entryOffset + 20), pixelsOffset);
        archive[entryOffset + gridOffset] = 1;
        archive[entryOffset + gridOffset + 1] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(entryOffset + cells32Offset), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(entryOffset + cells16Offset), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(entryOffset + cells8Offset), 0);
        archive.AsSpan(entryOffset + pixelsOffset, 64).Fill(0xA5);
        return archive;
    }
}
