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
        WriteTile(overlay, 0x20, visible: 1, anim: 0, margin: 4, depth: 2, frame: 0x47, cycle: 2, first: 0, last: 3, delay: 6, clut: 0, x: -8, y: 300, sortY: 310, cycleTime: 0);
        WriteTile(overlay, 0x20 + 18, visible: 0, anim: 0xFF, margin: 1, depth: 0, frame: 1, cycle: 0, first: 0, last: 0, delay: 0, clut: 2, x: 5, y: 6, sortY: 7, cycleTime: -1);

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
        WriteTile(overlay, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        WriteTile(overlay, 18, (byte)visible, 0, 1, 0, 0, (byte)cycle, (byte)first, (byte)last, 0, 0, 0, 0, 0, 0);

        FormatException error = Assert.Throws<FormatException>(() => StageTileDecoder.Decode(overlay, Base));
        Assert.Equal("unsupported StageTile record index=1", error.Message);
    }

    [Fact]
    public void RejectsATableRunningPastTheOverlayEnd()
    {
        byte[] overlay = new byte[20];
        WriteTile(overlay, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        FormatException error = Assert.Throws<FormatException>(() => StageTileDecoder.Decode(overlay, Base));
        Assert.Equal("StageTile range outside overlay address=0x800A5DF2", error.Message);
    }

    [Fact]
    public void RejectsAnAddressBelowTheOverlayImage()
    {
        Assert.Throws<FormatException>(() => StageTileDecoder.Decode(new byte[0x40], Base - 2));
    }

    internal static void WriteTile(byte[] buffer, int offset, byte visible, byte anim, byte margin, byte depth, byte frame, byte cycle,
        byte first, byte last, byte delay, byte clut, short x, short y, short sortY, short cycleTime)
    {
        byte[] head = [visible, anim, margin, depth, frame, cycle, first, last, delay, clut];
        head.CopyTo(buffer, offset);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset + 10), x);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset + 12), y);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset + 14), sortY);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset + 16), cycleTime);
    }
}
