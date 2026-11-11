using System.Buffers.Binary;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

public sealed class BattleSpriteAnimationTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(512, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 2)]
    public void InvalidOrTruncatedChannelsAreRejected(short flags, short stride, short duration)
    {
        var data = new byte[24];
        BinaryPrimitives.WriteInt16LittleEndian(data, flags);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2), stride);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(4), duration);
        Assert.Throws<FormatException>(() => BattleSpriteAnimationDecoder.Decode(data));
    }

    [Fact]
    public void TruncatedHeaderIsRejected() => Assert.Throws<FormatException>(() => BattleSpriteAnimationDecoder.Decode(new byte[23]));

    [Fact]
    public void AllNineChannelsReadSignedSamplesAndKeepTrailingTerminatorOutsideDuration()
    {
        var data = new byte[44];
        BinaryPrimitives.WriteInt16LittleEndian(data, 511);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2), 9);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(4), 1);
        for (short i = 0; i < 9; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(24 + (i * 2)), (short)-i);
        }

        data[42] = 255;
        Assert.Equal(Enumerable.Range(0, 9).Select(i => (short)-i), BattleSpriteAnimationDecoder.Decode(data)[0]);
    }

    [Fact]
    public void SelectedChannelsReplaceInitialValuesAtEachTime()
    {
        short[] words = [0x30, 2, 2, 7, 0, 3, 4, 4096, 4096, 0, 0, -123, 1000, 2000, 3000, 4000];
        byte[] data = new byte[words.Length * 2];
        for (int i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2), words[i]);
        }

        var frames = BattleSpriteAnimationDecoder.Decode(data);
        Assert.Equal(2, frames.Count);
        Assert.Equal(new short[] { 7, 0, 3, 4, 1000, 2000, 0, 0, -123 }, frames[0]);
        Assert.Equal(new short[] { 7, 0, 3, 4, 3000, 4000, 0, 0, -123 }, frames[1]);
    }
}
