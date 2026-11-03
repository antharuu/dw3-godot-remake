using System.Buffers.Binary;
using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Field;

public sealed class FieldStageTableDecoderTests
{
    [Fact]
    public void DecodeReturnsEveryRecordBeforeTheZeroModeTerminator()
    {
        byte[] table = BuildTable(
            (0x200, 17, 0x800A6010u),
            (0x201, 23, 0x800A6110u),
            (0, 0, 0));

        IReadOnlyList<FieldStageTableEntry> entries = FieldStageTableDecoder.Decode(table, 0, 3);

        Assert.Equal(2, entries.Count);
        Assert.Equal(new FieldStageTableEntry(0x200, 17, 0x800A6010u), entries[0]);
        Assert.Equal(new FieldStageTableEntry(0x201, 23, 0x800A6110u), entries[1]);
    }

    [Fact]
    public void DecodeRejectsATableWithoutATerminator()
    {
        byte[] table = BuildTable((0x200, 17, 0x800A6010u));

        FormatException exception = Assert.Throws<FormatException>(() =>
            FieldStageTableDecoder.Decode(table, 0, 1));

        Assert.Contains("terminator", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecodeRejectsAnOverlayIndexOutsideTheDiscFileTable()
    {
        byte[] table = BuildTable((-1, 17, 0x800A6010u), (0, 0, 0));

        FormatException exception = Assert.Throws<FormatException>(() =>
            FieldStageTableDecoder.Decode(table, 0, 2));

        Assert.Contains("mode", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] BuildTable(params (int Mode, int Overlay, uint Initializer)[] entries)
    {
        byte[] data = new byte[entries.Length * 12];
        for (int index = 0; index < entries.Length; index++)
        {
            int offset = index * 12;
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), entries[index].Mode);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset + 4, 4), entries[index].Overlay);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 8, 4), entries[index].Initializer);
        }

        return data;
    }
}
