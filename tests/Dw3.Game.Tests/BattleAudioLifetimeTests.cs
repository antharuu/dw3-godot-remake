using System.Buffers.Binary;
using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleAudioLifetimeTests
{
    [Fact]
    public void PositiveDuration_KeysOffAtDeadline_ZeroUsesNaturalCompletion()
    {
        var pool = Pool();
        var timed = (Voice)pool.Play(Entry() with { DurationFrames = 4 });
        var natural = (Voice)pool.Play(Entry());
        pool.Advance(3);
        Assert.False(timed.IsComplete);
        pool.Advance(1);
        Assert.True(timed.IsComplete);
        Assert.False(natural.IsComplete);
        natural.IsComplete = true;
        pool.Advance(0);
        Assert.Equal(0, pool.ActiveCount);
    }

    [Fact]
    public void Channels_PreemptLowerPriority_RejectLowerIncomingPriority()
    {
        var pool = Pool(2);
        var low = (Voice)pool.Play(Entry() with { Priority = 1 });
        var high = (Voice)pool.Play(Entry() with { Priority = 3 });
        pool.Play(Entry() with { Priority = 2 });
        Assert.True(low.IsComplete);
        Assert.False(high.IsComplete);
        Assert.Equal(2, pool.ActiveCount);
        Assert.Throws<IOException>(() => pool.Play(Entry() with { Priority = 0 }));
        pool.StopAll();
        Assert.True(high.IsComplete);
        Assert.Equal(0, pool.ActiveCount);
        pool.Play(Entry());
        Assert.Equal(1, pool.ActiveCount);
        pool.StopAll();
        Assert.Equal(0, pool.ActiveCount);
    }

    [Theory]
    [InlineData(-1, 1000, 0)]
    [InlineData(0, 1000, 1000)]
    [InlineData(1, 0, 1000)]
    public void Pan_IsAppliedToPhysicalStereoFrames(float pan, int expectedLeft, int expectedRight)
    {
        byte[] mono = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(mono, 1000);
        byte[] stereo = BattleAudioPcm.Pan(mono, pan);
        Assert.Equal(expectedLeft, BinaryPrimitives.ReadInt16LittleEndian(stereo));
        Assert.Equal(expectedRight, BinaryPrimitives.ReadInt16LittleEndian(stereo.AsSpan(2)));
    }

    [Fact]
    public void Pan_RejectsTruncatedPcm() => Assert.Throws<FormatException>(() => BattleAudioPcm.Pan([0], 0));

    private static BattleAudioVoicePool Pool(int capacity = 24) => new(_ => new Voice(), voice => ((Voice)voice).IsComplete = true, capacity);

    private static AudioProgramEntry Entry() => new(0, "fabricated.wav", 127, 64, 60, 0);

    private sealed class Voice : IVoiceHandle
    {
        public bool IsComplete { get; set; }
    }
}
