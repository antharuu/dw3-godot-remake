namespace Dw3.Formats.Battle;

/// <summary>Validates SEP MIDI-style events while retaining aggregate metadata only.</summary>
internal static class PlayStationSequenceEventReader
{
    /// <summary>Reads events through one valid end-of-track marker.</summary>
    /// <param name="data">Complete MP archive bytes.</param>
    /// <param name="eventOffset">Absolute offset of the first event.</param>
    /// <param name="entryEnd">Exclusive SEP entry end offset.</param>
    /// <returns>The exclusive terminator offset and safe aggregate counts.</returns>
    /// <exception cref="FormatException">An event is truncated, unsupported, or lacks a terminator.</exception>
    internal static PlayStationSequenceEventReadResult ReadToEndOfTrack(ReadOnlySpan<byte> data, int eventOffset, int entryEnd)
    {
        byte runningStatus = 0;
        int offset = eventOffset;
        int eventCount = 0;
        int channelCount = 0;
        int metaCount = 0;
        int systemCount = 0;
        int totalDeltaTicks = 0;
        while (offset < entryEnd)
        {
            int delta = ReadVariableLength(data, ref offset, entryEnd);
            AddDelta(ref totalDeltaTicks, delta);
            PlayStationSequenceEventKind kind = ReadEvent(data, ref offset, entryEnd, ref runningStatus, out bool endsTrack);
            Count(kind, ref eventCount, ref channelCount, ref metaCount, ref systemCount);
            if (endsTrack)
            {
                return new PlayStationSequenceEventReadResult(offset, new PlayStationSequenceEventSummary(eventCount, channelCount, metaCount, systemCount, totalDeltaTicks, true));
            }
        }

        throw new FormatException("SEP track ends before an end-of-track event.");
    }

    /// <summary>Reads one variable-length delta and validates its four-byte MIDI limit.</summary>
    /// <param name="data">Complete MP archive bytes.</param>
    /// <param name="offset">Current source offset, advanced after the delta.</param>
    /// <param name="entryEnd">Exclusive SEP entry end offset.</param>
    /// <returns>The decoded non-negative tick delta.</returns>
    /// <exception cref="FormatException">The delta is truncated or exceeds four bytes.</exception>
    private static int ReadVariableLength(ReadOnlySpan<byte> data, ref int offset, int entryEnd)
    {
        int value = 0;
        for (int index = 0; index < 4; index++)
        {
            byte current = ReadByte(data, ref offset, entryEnd);
            value = (value << 7) | (current & 0x7F);
            if ((current & 0x80) == 0)
            {
                return value;
            }
        }

        throw new FormatException("SEP event delta exceeds four bytes.");
    }

    /// <summary>Reads one event, including running status and payload bounds.</summary>
    /// <param name="data">Complete MP archive bytes.</param>
    /// <param name="offset">Current source offset, advanced after the event.</param>
    /// <param name="entryEnd">Exclusive SEP entry end offset.</param>
    /// <param name="runningStatus">Last channel status, updated or cleared by the event.</param>
    /// <param name="endsTrack">Receives whether the event is a valid end-of-track marker.</param>
    /// <returns>The safe category for aggregate counting.</returns>
    /// <exception cref="FormatException">The event has an invalid status, running status, or payload.</exception>
    private static PlayStationSequenceEventKind ReadEvent(ReadOnlySpan<byte> data, ref int offset, int entryEnd, ref byte runningStatus, out bool endsTrack)
    {
        byte first = ReadByte(data, ref offset, entryEnd);
        endsTrack = false;
        if (first < 0x80)
        {
            return ReadRunningStatusEvent(data, ref offset, entryEnd, runningStatus);
        }

        if (first < 0xF0)
        {
            runningStatus = first;
            ReadChannelData(data, ref offset, entryEnd, first, false);
            return PlayStationSequenceEventKind.Channel;
        }

        if (first is 0xFF or >= 0xF8 and <= 0xFE)
        {
            return ReadSystemEvent(data, ref offset, entryEnd, first, out endsTrack);
        }

        runningStatus = 0;
        return ReadSystemEvent(data, ref offset, entryEnd, first, out endsTrack);
    }

    /// <summary>Completes a channel event that omitted its status byte through MIDI running status.</summary>
    /// <param name="data">Complete MP archive bytes.</param>
    /// <param name="offset">Current source offset, advanced after remaining data bytes.</param>
    /// <param name="entryEnd">Exclusive SEP entry end offset.</param>
    /// <param name="runningStatus">Last available channel status.</param>
    /// <returns>The channel event category.</returns>
    /// <exception cref="FormatException">No channel status is available or a data byte is invalid.</exception>
    private static PlayStationSequenceEventKind ReadRunningStatusEvent(ReadOnlySpan<byte> data, ref int offset, int entryEnd, byte runningStatus)
    {
        if (runningStatus == 0)
        {
            throw new FormatException("SEP event uses running status before a channel status.");
        }

        ReadChannelData(data, ref offset, entryEnd, runningStatus, true);
        return PlayStationSequenceEventKind.Channel;
    }

    /// <summary>Consumes the remaining data bytes of one channel voice event.</summary>
    /// <param name="data">Complete MP archive bytes.</param>
    /// <param name="offset">Current source offset, advanced after channel data.</param>
    /// <param name="entryEnd">Exclusive SEP entry end offset.</param>
    /// <param name="status">Channel status byte.</param>
    /// <param name="hasFirstDataByte">Whether one data byte was already consumed as the event's first byte.</param>
    /// <exception cref="FormatException">The event payload is truncated or contains a status byte.</exception>
    private static void ReadChannelData(ReadOnlySpan<byte> data, ref int offset, int entryEnd, byte status, bool hasFirstDataByte)
    {
        int remaining = ChannelDataByteCount(status) - (hasFirstDataByte ? 1 : 0);
        ReadDataBytes(data, ref offset, entryEnd, remaining);
    }

    /// <summary>Reads a system or meta event and its bounded payload.</summary>
    /// <param name="data">Complete MP archive bytes.</param>
    /// <param name="offset">Current source offset, advanced after the event.</param>
    /// <param name="entryEnd">Exclusive SEP entry end offset.</param>
    /// <param name="status">System status byte.</param>
    /// <param name="endsTrack">Receives whether this is the sole valid end-of-track form.</param>
    /// <returns>The safe system or meta event category.</returns>
    /// <exception cref="FormatException">The system event is unsupported or has an invalid payload.</exception>
    private static PlayStationSequenceEventKind ReadSystemEvent(ReadOnlySpan<byte> data, ref int offset, int entryEnd, byte status, out bool endsTrack)
    {
        endsTrack = false;
        if (status == 0xFF)
        {
            byte type = ReadByte(data, ref offset, entryEnd);
            if (type == 0x51)
            {
                SkipPayload(ref offset, entryEnd, 3);
                return PlayStationSequenceEventKind.Meta;
            }

            int length = ReadVariableLength(data, ref offset, entryEnd);
            SkipPayload(ref offset, entryEnd, length);
            endsTrack = type == 0x2F;
            if (endsTrack && length != 0)
            {
                throw new FormatException("SEP end-of-track event has a payload.");
            }

            return PlayStationSequenceEventKind.Meta;
        }

        if (status is 0xF0 or 0xF7)
        {
            int length = ReadVariableLength(data, ref offset, entryEnd);
            SkipPayload(ref offset, entryEnd, length);
            return PlayStationSequenceEventKind.System;
        }

        ReadDataBytes(data, ref offset, entryEnd, SystemDataByteCount(status));
        return PlayStationSequenceEventKind.System;
    }

    /// <summary>Returns the data-byte count for a valid channel voice status.</summary>
    /// <param name="status">Channel status byte.</param>
    /// <returns>One for program pressure events, otherwise two.</returns>
    private static int ChannelDataByteCount(byte status) => status is >= 0xC0 and <= 0xDF ? 1 : 2;

    /// <summary>Returns the data-byte count for supported MIDI system common or real-time statuses.</summary>
    /// <param name="status">System status byte.</param>
    /// <returns>The number of seven-bit data bytes following the status.</returns>
    /// <exception cref="FormatException">The status is reserved or unsupported.</exception>
    private static int SystemDataByteCount(byte status) => status switch
    {
        0xF1 or 0xF3 => 1,
        0xF2 => 2,
        0xF6 or 0xF8 or 0xF9 or 0xFA or 0xFB or 0xFC or 0xFD or 0xFE => 0,
        _ => throw new FormatException("SEP event uses an unsupported system status."),
    };

    /// <summary>Reads and validates a fixed number of seven-bit MIDI data bytes.</summary>
    /// <param name="data">Complete MP archive bytes.</param>
    /// <param name="offset">Current source offset, advanced after the data.</param>
    /// <param name="entryEnd">Exclusive SEP entry end offset.</param>
    /// <param name="count">Required number of data bytes.</param>
    /// <exception cref="FormatException">A data byte is truncated or has its status bit set.</exception>
    private static void ReadDataBytes(ReadOnlySpan<byte> data, ref int offset, int entryEnd, int count)
    {
        for (int index = 0; index < count; index++)
        {
            byte value = ReadByte(data, ref offset, entryEnd);
            if ((value & 0x80) != 0)
            {
                throw new FormatException($"SEP event data byte at 0x{offset - 1:X} has its status bit set.");
            }
        }
    }

    /// <summary>Reads one byte after proving that it lies inside the SEP entry.</summary>
    /// <param name="data">Complete MP archive bytes.</param>
    /// <param name="offset">Current source offset, advanced after the byte.</param>
    /// <param name="entryEnd">Exclusive SEP entry end offset.</param>
    /// <returns>The next source byte.</returns>
    /// <exception cref="FormatException">No byte remains inside the SEP entry.</exception>
    private static byte ReadByte(ReadOnlySpan<byte> data, ref int offset, int entryEnd)
    {
        if (offset >= entryEnd)
        {
            throw new FormatException("SEP event is truncated.");
        }

        return data[offset++];
    }

    /// <summary>Skips a validated variable-length payload without retaining its proprietary contents.</summary>
    /// <param name="offset">Current source offset, advanced after the payload.</param>
    /// <param name="entryEnd">Exclusive SEP entry end offset.</param>
    /// <param name="length">Declared payload length.</param>
    /// <exception cref="FormatException">The payload reaches beyond the SEP entry.</exception>
    private static void SkipPayload(ref int offset, int entryEnd, int length)
    {
        if (length > entryEnd - offset)
        {
            throw new FormatException("SEP event payload is truncated.");
        }

        offset += length;
    }

    /// <summary>Adds one delta while rejecting integer overflow in the safe aggregate.</summary>
    /// <param name="total">Running tick sum.</param>
    /// <param name="delta">New non-negative tick delta.</param>
    /// <exception cref="FormatException">The aggregate exceeds the supported integer range.</exception>
    private static void AddDelta(ref int total, int delta)
    {
        if (delta > int.MaxValue - total)
        {
            throw new FormatException("SEP track total delta exceeds the supported range.");
        }

        total += delta;
    }

    /// <summary>Adds one event to the aggregate counters.</summary>
    /// <param name="kind">Safe category of the decoded event.</param>
    /// <param name="eventCount">Total event count.</param>
    /// <param name="channelCount">Channel event count.</param>
    /// <param name="metaCount">Meta event count.</param>
    /// <param name="systemCount">System event count.</param>
    /// <exception cref="FormatException">The source has too many events to summarize safely.</exception>
    private static void Count(PlayStationSequenceEventKind kind, ref int eventCount, ref int channelCount, ref int metaCount, ref int systemCount)
    {
        if (eventCount == int.MaxValue)
        {
            throw new FormatException("SEP track has too many events.");
        }

        eventCount++;
        switch (kind)
        {
            case PlayStationSequenceEventKind.Channel:
                channelCount++;
                break;
            case PlayStationSequenceEventKind.Meta:
                metaCount++;
                break;
            default:
                systemCount++;
                break;
        }
    }
}
