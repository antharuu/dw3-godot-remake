using System.Buffers.Binary;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

/// <summary>Synthetic contract tests for PlayStation SEP sequences nested after battle VAB headers.</summary>
public sealed class PlayStationSequenceDecoderTests
{
    /// <summary>Provides malformed archives that exercise every boundary of the SEP contract.</summary>
    public static IEnumerable<object[]> MalformedArchives()
    {
        byte[] invalidSignature = Archive([0x00, 0xFF, 0x2F, 0x00]);
        invalidSignature[40] = 0;
        yield return [invalidSignature];

        byte[] zeroResolution = Archive([0x00, 0xFF, 0x2F, 0x00]);
        zeroResolution[48] = 0;
        zeroResolution[49] = 0;
        yield return [zeroResolution];

        byte[] incoherentTrackNumber = Archive([0x00, 0xFF, 0x2F, 0x00], [0x00, 0xFF, 0x2F, 0x00]);
        incoherentTrackNumber[64] = 2;
        yield return [incoherentTrackNumber];

        yield return [Archive([0x00, 0x90, 0x3C])];
        yield return [Archive([0x00, 0x3C, 0x00])];
        yield return [Archive([0x00, 0xFF, 0x2F, 0x01, 0x00])];
        yield return [Archive([129, 128, 128, 128, 0, 255, 47, 0])];
    }

    [Fact]
    public void Decode_ReadsOffsetsTracksAndSafeEventSummaries()
    {
        byte[] archive = Archive(
            [0, 144, 60, 64, 10, 128, 60, 0, 0, 255, 47, 0],
            [0x00, 0xC1, 0x07, 0x00, 0xFF, 0x2F, 0x00]);

        PlayStationSequence sequence = PlayStationSequenceDecoder.Decode(archive);

        Assert.Equal(40, sequence.Offset);
        Assert.Equal(2, sequence.Tracks.Count);
        Assert.Equal(0, sequence.Tracks[0].Number);
        Assert.Equal(59, sequence.Tracks[0].EventsOffset);
        Assert.Equal(3, sequence.Tracks[0].Events.EventCount);
        Assert.Equal(2, sequence.Tracks[0].Events.ChannelEventCount);
        Assert.Equal(1, sequence.Tracks[0].Events.MetaEventCount);
        Assert.Equal(10, sequence.Tracks[0].Events.TotalDeltaTicks);
        Assert.True(sequence.Tracks[0].Events.HasEndOfTrack);
        Assert.Equal(1, sequence.Tracks[1].Number);
        Assert.Equal(1, sequence.Tracks[1].Events.ChannelEventCount);
        Assert.True(sequence.Tracks[1].Events.HasEndOfTrack);
    }

    [Fact]
    public void Decode_AcceptsZeroPaddingAfterTheLastTrack()
    {
        PlayStationSequence sequence = PlayStationSequenceDecoder.Decode(ArchiveWithPadding(3, [0x00, 0xFF, 0x2F, 0x00]));

        Assert.Single(sequence.Tracks);
        Assert.True(sequence.Tracks[0].Events.HasEndOfTrack);
    }

    [Fact]
    public void Decode_PreservesRunningStatusAcrossMetaEvents()
    {
        PlayStationSequence sequence = PlayStationSequenceDecoder.Decode(
            Archive([0, 144, 60, 64, 0, 255, 81, 5, 0, 0, 0, 176, 1, 6, 0, 61, 64, 0, 255, 47, 0]));

        Assert.Equal(5, sequence.Tracks[0].Events.EventCount);
        Assert.Equal(3, sequence.Tracks[0].Events.ChannelEventCount);
        Assert.Equal(2, sequence.Tracks[0].Events.MetaEventCount);
    }

    [Theory]
    [MemberData(nameof(MalformedArchives))]
    public void Decode_RejectsMalformedSignaturesHeadersTracksAndEvents(byte[] archive)
    {
        Assert.Throws<FormatException>(() => PlayStationSequenceDecoder.Decode(archive));
    }

    private static byte[] Archive(params byte[][] tracks)
    {
        byte[] vab = Vab();
        var sequence = new List<byte>([112, 81, 69, 83, 0, 0, 0, 0]);
        WriteFirstTrackHeader(sequence);
        sequence.AddRange(tracks[0]);
        for (int index = 1; index < tracks.Length; index++)
        {
            WriteFollowingTrackHeader(sequence, index);
            sequence.AddRange(tracks[index]);
        }

        byte[] archive = new byte[8 + vab.Length + sequence.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(archive, 8U);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), (uint)(8 + vab.Length));
        vab.CopyTo(archive, 8);
        sequence.CopyTo(archive, 8 + vab.Length);
        return archive;
    }

    private static byte[] ArchiveWithPadding(int paddingBytes, params byte[][] tracks)
    {
        byte[] archive = Archive(tracks);
        return [.. archive, .. new byte[paddingBytes]];
    }

    private static byte[] Vab()
    {
        byte[] vab = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(vab, 0x56414270U);
        BinaryPrimitives.WriteUInt32LittleEndian(vab.AsSpan(12), 32U);
        return vab;
    }

    private static void WriteFirstTrackHeader(List<byte> destination)
    {
        destination.Add(0x01);
        destination.Add(0xE0);
        destination.Add(0x02);
        destination.Add(0x49);
        destination.Add(0xF0);
        destination.AddRange([0x04, 0x04, 0x00, 0x00, 0x00, 0x00]);
    }

    private static void WriteFollowingTrackHeader(List<byte> destination, int number)
    {
        destination.Add(0);
        destination.Add((byte)number);
        destination.Add(0x01);
        destination.Add(0xE0);
        destination.Add(0x02);
        destination.Add(0x49);
        destination.Add(0xF0);
        destination.AddRange([0x04, 0x04, 0x00, 0x00, 0x00, 0x00]);
    }
}
