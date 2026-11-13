using Dw3Production.Conversion;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class SequenceNrpnDecoderTests
{
    [Fact]
    public void Apply_AuthoredMusicTriplets_YieldGlobalReverbTypeThenDepthAndEndlessLoopMarkers()
    {
        var decoder = new SequenceNrpnDecoder();
        (double Seconds, int Controller, int Value)[] events =
        [
            (0, 99, 16), (0.004, 98, 15), (0.007, 6, 3),
            (0.011, 99, 16), (0.014, 98, 16), (0.018, 6, 56),
            (0.043, 99, 20), (0.044, 6, 127), (65.798, 99, 30),
        ];

        var changes = events.Select(item => decoder.Apply(item.Seconds, item.Controller, item.Value)).OfType<AudioReverbChange>().ToArray();

        Assert.Equal([new AudioReverbChange(AudioReverbParameter.Type, 3), new AudioReverbChange(AudioReverbParameter.Depth, 56)], changes);
        Assert.Equal(0.043, decoder.LoopStartSeconds);
        Assert.Equal(65.798, decoder.LoopEndSeconds);
    }

    [Fact]
    public void Apply_SingleToneSelection_StillChangesTheGlobalReverb()
    {
        var decoder = new SequenceNrpnDecoder();
        decoder.Apply(0, 99, 2);
        decoder.Apply(0, 98, 16);

        Assert.Equal(new AudioReverbChange(AudioReverbParameter.Depth, 0), decoder.Apply(0, 6, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    public void Apply_UnqualifiedAttribute_IsRefusedInsteadOfIgnored(int attribute)
    {
        var decoder = new SequenceNrpnDecoder();
        decoder.Apply(0, 99, 16);
        decoder.Apply(0, 98, attribute);

        Assert.Throws<FormatException>(() => decoder.Apply(0, 6, 1));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(10)]
    public void Apply_EchoDelayOrUnknownPreset_IsRefused(int preset)
    {
        var decoder = new SequenceNrpnDecoder();
        decoder.Apply(0, 99, 16);
        decoder.Apply(0, 98, 15);

        Assert.Throws<FormatException>(() => decoder.Apply(0, 6, preset));
    }

    [Fact]
    public void Apply_FiniteLoopCount_IsRefused()
    {
        var decoder = new SequenceNrpnDecoder();
        decoder.Apply(0, 99, 20);

        Assert.Throws<FormatException>(() => decoder.Apply(0, 6, 3));
    }

    [Fact]
    public void Apply_IncompleteOrMisorderedSelections_AreRefused()
    {
        Assert.Throws<FormatException>(() => new SequenceNrpnDecoder().Apply(0, 6, 3));
        Assert.Throws<FormatException>(() => new SequenceNrpnDecoder().Apply(0, 98, 15));
        Assert.Throws<FormatException>(() => new SequenceNrpnDecoder().Apply(0, 99, 30));
        Assert.Throws<FormatException>(() => new SequenceNrpnDecoder().Apply(0, 99, 40));
        Assert.Throws<FormatException>(() => new SequenceNrpnDecoder().Apply(0, 7, 100));
    }

    [Fact]
    public void Apply_NewParameterSelection_ClearsThePreviousAttribute()
    {
        var decoder = new SequenceNrpnDecoder();
        decoder.Apply(0, 99, 16);
        decoder.Apply(0, 98, 16);
        decoder.Apply(0, 99, 16);

        Assert.Throws<FormatException>(() => decoder.Apply(0, 6, 56));
    }

    [Fact]
    public void Handles_OnlyTheNrpnTriplet()
    {
        Assert.True(SequenceNrpnDecoder.Handles(99));
        Assert.True(SequenceNrpnDecoder.Handles(98));
        Assert.True(SequenceNrpnDecoder.Handles(6));
        Assert.False(SequenceNrpnDecoder.Handles(7));
    }
}
