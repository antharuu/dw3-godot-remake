using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>Decodes the SEP entry stored after the VAB header entry of a battle MP archive.</summary>
public static class PlayStationSequenceDecoder
{
    private const int SignatureBytes = 4;
    private const int FirstTrackHeaderBytes = 15;
    private const int FollowingTrackHeaderBytes = 13;
    private const uint SepMagic = 0x70514553;

    /// <summary>Decodes every SEP track after validating the MP archive and its first VAB entry.</summary>
    /// <param name="archive">Raw MP archive bytes with VAB first and SEP second.</param>
    /// <returns>Offsets and non-proprietary event summaries for every SEP track.</returns>
    /// <exception cref="FormatException">The MP archive, VAB, SEP header, track layout, or event stream is invalid.</exception>
    public static PlayStationSequence Decode(ReadOnlySpan<byte> archive)
    {
        IReadOnlyList<ArchiveRange> entries = ModelArchive.ReadBounds(archive);
        _ = SoundBankHeaderDecoder.Decode(archive);
        ArchiveRange sequenceEntry = ReadSequenceEntry(entries);
        return DecodeEntry(archive, sequenceEntry);
    }

    /// <summary>Decodes every SEP member following the VAB header in archive order.</summary>
    /// <param name="archive">Raw MP archive bytes.</param>
    /// <returns>Validated sequence groups with their original track identities.</returns>
    public static IReadOnlyList<PlayStationSequence> DecodeAll(ReadOnlySpan<byte> archive)
    {
        IReadOnlyList<ArchiveRange> entries = ModelArchive.ReadBounds(archive);
        _ = SoundBankHeaderDecoder.Decode(archive);
        _ = ReadSequenceEntry(entries);
        var groups = new List<PlayStationSequence>();
        foreach (var entry in entries.Skip(1))
        {
            groups.Add(DecodeEntry(archive, entry));
        }

        return groups;
    }

    /// <summary>Gets the mandatory second non-empty MP entry that contains the SEP bytes.</summary>
    /// <param name="entries">Validated MP archive ranges.</param>
    /// <returns>The SEP entry bounds.</returns>
    /// <exception cref="FormatException">The second entry is absent or empty.</exception>
    private static ArchiveRange ReadSequenceEntry(IReadOnlyList<ArchiveRange> entries)
    {
        if (entries.Count < 2 || entries[1].End <= entries[1].Start)
        {
            throw new FormatException("Sound bank archive has no SEP entry after its VAB header.");
        }

        return entries[1];
    }

    /// <summary>Decodes all positionally concatenated track headers and event streams in one SEP entry.</summary>
    /// <param name="archive">Complete MP archive bytes.</param>
    /// <param name="entry">Validated SEP entry bounds.</param>
    /// <returns>Validated sequence metadata.</returns>
    /// <exception cref="FormatException">The SEP signature or one of its tracks is invalid.</exception>
    private static PlayStationSequence DecodeEntry(ReadOnlySpan<byte> archive, ArchiveRange entry)
    {
        if (entry.End - entry.Start < SignatureBytes + FirstTrackHeaderBytes || BinaryPrimitives.ReadUInt32BigEndian(archive.Slice(entry.Start, SignatureBytes)) != SepMagic)
        {
            throw new FormatException("SEP entry has an invalid signature or truncated first track header.");
        }

        var tracks = new List<PlayStationSequenceTrack>();
        (PlayStationSequenceTrack firstTrack, int offset) = ReadFirstTrack(archive, entry.Start + SignatureBytes, entry.End);
        tracks.Add(firstTrack);
        while (offset < entry.End)
        {
            if (IsZeroPadding(archive, offset, entry.End))
            {
                break;
            }

            (PlayStationSequenceTrack track, int nextOffset) = ReadFollowingTrack(archive, offset, entry.End, tracks.Count);
            tracks.Add(track);
            offset = nextOffset;
        }

        return new PlayStationSequence(entry.Start, entry.End - entry.Start, tracks);
    }

    /// <summary>Determines whether the remaining entry bytes are alignment padding rather than a partial track.</summary>
    /// <param name="data">Complete MP archive bytes.</param>
    /// <param name="offset">First possible padding byte.</param>
    /// <param name="entryEnd">Exclusive SEP entry end offset.</param>
    /// <returns><see langword="true"/> when every remaining byte is zero.</returns>
    private static bool IsZeroPadding(ReadOnlySpan<byte> data, int offset, int entryEnd)
    {
        for (int index = offset; index < entryEnd; index++)
        {
            if (data[index] != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads the initial SEP header, which declares the implicit zeroth track.</summary>
    /// <param name="archive">Complete MP archive bytes.</param>
    /// <param name="headerOffset">Absolute offset immediately after the SEP signature.</param>
    /// <param name="entryEnd">Exclusive SEP entry end offset.</param>
    /// <returns>The initial track metadata and exclusive offset after its end-of-track event.</returns>
    /// <exception cref="FormatException">The first header is truncated or declares unsupported timing.</exception>
    private static (PlayStationSequenceTrack Track, int NextOffset) ReadFirstTrack(ReadOnlySpan<byte> archive, int headerOffset, int entryEnd)
    {
        if (headerOffset > entryEnd - FirstTrackHeaderBytes)
        {
            throw new FormatException("SEP first track header is truncated.");
        }

        uint version = BinaryPrimitives.ReadUInt32BigEndian(archive.Slice(headerOffset, 4));
        ushort ticksPerQuarterNote = BinaryPrimitives.ReadUInt16BigEndian(archive.Slice(headerOffset + 4, 2));
        uint tempo = ReadThreeByteInteger(archive, headerOffset + 6);
        if (version != 0 || ticksPerQuarterNote == 0 || tempo == 0)
        {
            throw new FormatException("SEP first track header declares an unsupported version or timing value.");
        }

        return ReadTrackEvents(archive, headerOffset, headerOffset + FirstTrackHeaderBytes, entryEnd, 0, ticksPerQuarterNote, tempo);
    }

    /// <summary>Reads one numbered SEP track header that follows a prior end-of-track event.</summary>
    /// <param name="archive">Complete MP archive bytes.</param>
    /// <param name="headerOffset">Absolute offset of the numbered track header.</param>
    /// <param name="entryEnd">Exclusive SEP entry end offset.</param>
    /// <param name="expectedNumber">Required sequential track number.</param>
    /// <returns>The track metadata and exclusive offset after its end-of-track event.</returns>
    /// <exception cref="FormatException">The header is truncated, invalid, or followed by invalid events.</exception>
    private static (PlayStationSequenceTrack Track, int NextOffset) ReadFollowingTrack(ReadOnlySpan<byte> archive, int headerOffset, int entryEnd, int expectedNumber)
    {
        if (headerOffset > entryEnd - FollowingTrackHeaderBytes)
        {
            throw new FormatException("SEP track header is truncated.");
        }

        ushort number = BinaryPrimitives.ReadUInt16BigEndian(archive.Slice(headerOffset, 2));
        ushort ticksPerQuarterNote = BinaryPrimitives.ReadUInt16BigEndian(archive.Slice(headerOffset + 2, 2));
        uint tempo = ReadThreeByteInteger(archive, headerOffset + 4);
        if (number != expectedNumber || ticksPerQuarterNote == 0 || tempo == 0)
        {
            throw new FormatException("SEP track header declares an incoherent number or timing value.");
        }

        return ReadTrackEvents(archive, headerOffset, headerOffset + FollowingTrackHeaderBytes, entryEnd, number, ticksPerQuarterNote, tempo);
    }

    /// <summary>Reads one track's events after its fixed metadata has been validated.</summary>
    /// <param name="archive">Complete MP archive bytes.</param>
    /// <param name="headerOffset">Absolute offset of the applicable track header.</param>
    /// <param name="eventsOffset">Absolute offset of the first event.</param>
    /// <param name="entryEnd">Exclusive SEP entry end offset.</param>
    /// <param name="number">Validated zero-based track number.</param>
    /// <param name="ticksPerQuarterNote">Validated track timing resolution.</param>
    /// <param name="tempo">Validated track tempo.</param>
    /// <returns>The track metadata and exclusive offset after its end-of-track event.</returns>
    private static (PlayStationSequenceTrack Track, int NextOffset) ReadTrackEvents(
        ReadOnlySpan<byte> archive,
        int headerOffset,
        int eventsOffset,
        int entryEnd,
        ushort number,
        ushort ticksPerQuarterNote,
        uint tempo)
    {
        PlayStationSequenceEventReadResult result = PlayStationSequenceEventReader.ReadToEndOfTrack(archive, eventsOffset, entryEnd);
        var track = new PlayStationSequenceTrack(number, headerOffset, eventsOffset, result.NextOffset, ticksPerQuarterNote, tempo, result.Summary);
        return (track, result.NextOffset);
    }

    /// <summary>Reads a big-endian unsigned integer stored in three bytes.</summary>
    /// <param name="data">Bytes that contain the integer.</param>
    /// <param name="offset">Offset of the most significant byte.</param>
    /// <returns>The decoded integer.</returns>
    private static uint ReadThreeByteInteger(ReadOnlySpan<byte> data, int offset) =>
        ((uint)data[offset] << 16) | ((uint)data[offset + 1] << 8) | data[offset + 2];
}
