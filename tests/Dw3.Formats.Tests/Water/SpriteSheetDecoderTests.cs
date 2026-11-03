using System.Buffers.Binary;
using Dw3.Formats.Water;
using Xunit;

namespace Dw3.Formats.Tests.Water;

public sealed class SpriteSheetDecoderTests
{
    [Fact]
    public void SlicesTheRequestedArchiveEntryBetweenItsTwoOffsets()
    {
        byte[] archive = [.. U32(12), .. U32(20), .. U32(28), .. "01234567"u8.ToArray(), .. "89abcdef"u8.ToArray()];

        Assert.Equal("01234567"u8.ToArray(), SpriteSheetDecoder.ReadArchiveEntry(archive, 0).ToArray());
        Assert.Equal("89abcdef"u8.ToArray(), SpriteSheetDecoder.ReadArchiveEntry(archive, 1).ToArray());
        FormatException error = Assert.Throws<FormatException>(() => SpriteSheetDecoder.ReadArchiveEntry(archive, 2));
        Assert.Equal("sprite sheet archive offsets invalid", error.Message);
        Assert.Equal(
            "sprite sheet archive offset table truncated",
            Assert.Throws<FormatException>(() => SpriteSheetDecoder.ReadArchiveEntry(archive, 6)).Message);
    }

    [Fact]
    public void RejectsAnEmptyOrOutOfRangeArchiveEntry()
    {
        byte[] archive = [.. U32(12), .. U32(12), .. U32(40), 0, 0, 0, 0];

        FormatException error = Assert.Throws<FormatException>(() => SpriteSheetDecoder.ReadArchiveEntry(archive, 0));
        Assert.Equal("sprite sheet archive offsets invalid", error.Message);
    }

    [Fact]
    public void DecodesPartsAndFrameIdsWithTheirOffsets()
    {
        byte[] sheet = BuildSheet(parts: [[1, 2, 3, 4, 5, 6, 0], [7, 8, 9, 10, 11, 12, 1]], frameIds: [0x10, 0x11], frames: [[0xAA], [0xBB, 0xCC]]);

        SpriteSheet decoded = SpriteSheetDecoder.Decode(sheet);

        Assert.Equal([new SpritePart(1, 2, 3, 4, 5, 6, 0), new SpritePart(7, 8, 9, 10, 11, 12, 1)], decoded.Parts);
        Assert.Equal(0xAA, decoded.Frames[0x10].Span[0]);
        Assert.Equal(0xBB, decoded.Frames[0x11].Span[0]);
        Assert.Equal(0xCC, decoded.Frames[0x11].Span[1]);
    }

    [Fact]
    public void AcceptsTheTwoBytePadAfterAnOddPartCount()
    {
        byte[] sheet = BuildSheet(parts: [[1, 2, 3, 4, 5, 6, 0]], frameIds: [0x01], frames: [[0xAA]]);

        Assert.Single(SpriteSheetDecoder.Decode(sheet).Parts);
    }

    [Fact]
    public void RejectsAFrameIdTableWithoutTerminator()
    {
        byte[] sheet = [.. U32(8), .. U32(9), 1, 2, 3, 4];

        FormatException error = Assert.Throws<FormatException>(() => SpriteSheetDecoder.Decode(sheet));
        Assert.Equal("sprite sheet has no frame terminator", error.Message);
    }

    [Fact]
    public void RejectsFrameOffsetsPointingIntoTheHeader()
    {
        byte[] sheet = BuildSheet(parts: [[1, 2, 3, 4, 5, 6, 0]], frameIds: [0x01], frames: [[0xAA]]);
        BinaryPrimitives.WriteUInt32LittleEndian(sheet.AsSpan(8), 2);

        FormatException error = Assert.Throws<FormatException>(() => SpriteSheetDecoder.Decode(sheet));
        Assert.Equal("sprite sheet frame offset invalid", error.Message);
    }

    [Fact]
    public void RejectsInvalidHeaderOffsets()
    {
        FormatException error = Assert.Throws<FormatException>(() => SpriteSheetDecoder.Decode(new byte[] { 8, 0, 0, 0, 4, 0, 0, 0, 0xFF, 0, 0, 0 }));
        Assert.Equal("sprite sheet offsets invalid", error.Message);
        Assert.Equal("sprite sheet truncated", Assert.Throws<FormatException>(() => SpriteSheetDecoder.Decode(new byte[11])).Message);
    }

    /// <summary>Lays out header, one frame-offset slot per id, part records, ids, then frame bodies.</summary>
    internal static byte[] BuildSheet(short[][] parts, byte[] frameIds, byte[][] frames)
    {
        int partsOffset = 8 + (4 * frameIds.Length);
        int partBytes = parts.Length * 14;
        int idsOffset = partsOffset + partBytes + (partBytes % 4 == 0 ? 0 : 2);
        int framesOffset = idsOffset + frameIds.Length + 1;
        var buffer = new List<byte>();
        buffer.AddRange(U32(partsOffset));
        buffer.AddRange(U32(idsOffset));
        int cursor = framesOffset;
        foreach (byte[] frame in frames)
        {
            buffer.AddRange(U32(cursor));
            cursor += frame.Length;
        }

        foreach (short[] part in parts)
        {
            foreach (short value in part)
            {
                buffer.AddRange(BitConverter.GetBytes(value));
            }
        }

        while (buffer.Count < idsOffset)
        {
            buffer.Add(0);
        }

        buffer.AddRange(frameIds);
        buffer.Add(0xFF);
        foreach (byte[] frame in frames)
        {
            buffer.AddRange(frame);
        }

        return [.. buffer];
    }

    internal static byte[] U32(int value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }
}
