using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

public sealed class SequenceMidiDecoderTests
{
    [Fact]
    public void Decode_PreservesRunningStatus_Tempo_Controllers_AndSystemIdentity()
    {
        byte[] data = [0, 0xC3, 7, 2, 0x93, 60, 100, 4, 62, 110, 0, 0xFF, 0x51, 3, 0xD0, 0x90, 1, 64, 0, 0, 0xB3, 10, 64, 0, 0xF0, 2, 1, 2, 0, 0xF1, 4, 0, 0xF2, 4, 5, 0, 0xF3, 6, 0, 0xF8, 0, 0xFF, 0x2F, 0];
        var events = SequenceMidiDecoder.Decode(data, Track(data.Length));
        Assert.Equal(new SequenceMidiEvent(0, 0xC3, 7, 0), events[0]);
        Assert.Equal(new SequenceMidiEvent(4, 0x93, 62, 110), events[2]);
        Assert.Equal(new SequenceMidiEvent(0, 255, 0x51, 250000), events[3]);
        Assert.Equal(new SequenceMidiEvent(1, 0x93, 64, 0), events[4]);
        Assert.Equal(new SequenceMidiEvent(0, 0xB3, 10, 64), events[5]);
        Assert.Equal(0xF0, events[6].Status);
        Assert.Equal(0xF8, events[^2].Status);
        Assert.Equal(new SequenceMidiEvent(0, 255, 0x2F, 0), events[^1]);
    }

    [Theory]
    [InlineData(new byte[] { 0, 60, 100 })]
    [InlineData(new byte[] { 0, 0x90, 60 })]
    [InlineData(new byte[] { 0x81, 0x81, 0x81, 0x81, 0, 0xFF, 0x2F, 0 })]
    [InlineData(new byte[] { 0, 0xFF, 0x2F, 1, 0 })]
    [InlineData(new byte[] { 0, 0xF0, 127, 0 })]
    public void Decode_RejectsMalformedEvents(byte[] data) => Assert.Throws<FormatException>(() => SequenceMidiDecoder.Decode(data, Track(data.Length)));

    private static PlayStationSequenceTrack Track(int end) => new(0, 0, 0, end, 480, 250000, new(0, 0, 0, 0, 0, true));
}
