using System.Buffers.Binary;
using Dw3.Formats.Water;
using Xunit;

namespace Dw3.Formats.Tests.Water;

public sealed class StageTileDecoderTests
{
    private const uint Base = 0x800A5DE0;

    [Fact]
    public void DecodesRecordsUpToTheZeroMarginTerminator()
    {
        byte[] overlay = new byte[0x80];
        WriteTile(overlay, 0x20, [1, 0, 4, 2, 0x47, 2, 0, 3, 6, 0], [-8, 300, 310, 0]);
        WriteTile(overlay, 0x20 + 18, [0, 0xFF, 1, 0, 1, 0, 0, 0, 0, 2], [5, 6, 7, -1]);

        IReadOnlyList<StageTile> tiles = StageTileDecoder.Decode(overlay, Base + 0x20);

        Assert.Equal(2, tiles.Count);
        Assert.Equal(new StageTile(1, 0, 4, 2, 0x47, 2, 0, 3, 6, 0, -8, 300, 310, 0, Base + 0x20), tiles[0]);
        Assert.Equal(new StageTile(0, 0xFF, 1, 0, 1, 0, 0, 0, 0, 2, 5, 6, 7, -1, Base + 0x32), tiles[1]);
    }

    [Fact]
    public void RejectsATableThatBeginsWithTheTerminator()
    {
        FormatException error = Assert.Throws<FormatException>(() => StageTileDecoder.Decode(new byte[0x40], Base));
        Assert.Equal("StageTile table begins with terminator", error.Message);
    }

    [Theory]
    [InlineData(2, 0, 0, 0)]
    [InlineData(1, 4, 0, 0)]
    [InlineData(1, 1, 3, 2)]
    public void RejectsUnsupportedRecordFields(int visible, int cycle, int first, int last)
    {
        byte[] overlay = new byte[0x40];
        WriteTile(overlay, 0, [1, 0, 1, 0, 0, 0, 0, 0, 0, 0], [0, 0, 0, 0]);
        WriteTile(overlay, 18, [(byte)visible, 0, 1, 0, 0, (byte)cycle, (byte)first, (byte)last, 0, 0], [0, 0, 0, 0]);

        FormatException error = Assert.Throws<FormatException>(() => StageTileDecoder.Decode(overlay, Base));
        Assert.Equal("unsupported StageTile record index=1", error.Message);
    }

    [Fact]
    public void RejectsATableRunningPastTheOverlayEnd()
    {
        byte[] overlay = new byte[20];
        WriteTile(overlay, 0, [1, 0, 1, 0, 0, 0, 0, 0, 0, 0], [0, 0, 0, 0]);

        FormatException error = Assert.Throws<FormatException>(() => StageTileDecoder.Decode(overlay, Base));
        Assert.Equal("StageTile range outside overlay address=0x800A5DF2", error.Message);
    }

    [Fact]
    public void RejectsAnAddressBelowTheOverlayImage()
    {
        Assert.Throws<FormatException>(() => StageTileDecoder.Decode(new byte[0x40], Base - 2));
    }

    internal static void WriteTile(byte[] buffer, int offset, byte[] header, short[] position)
    {
        header.CopyTo(buffer, offset);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset + 10), position[0]);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset + 12), position[1]);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset + 14), position[2]);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset + 16), position[3]);
    }
}
