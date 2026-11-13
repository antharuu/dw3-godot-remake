namespace Dw3.Formats.Battle;

/// <summary>Prepares validated SEP channel events without requiring container parsing at runtime.</summary>
public static class SequenceMidiDecoder
{
    /// <summary>Decodes one validated track into relative events, preserving unsupported statuses explicitly.</summary>
    /// <param name="archive">Original archive bytes read during extraction only.</param>
    /// <param name="track">Validated offsets and timing metadata.</param>
    /// <returns>Complete event stream including the end marker.</returns>
    public static IReadOnlyList<SequenceMidiEvent> Decode(ReadOnlySpan<byte> archive, PlayStationSequenceTrack track)
    {
        _ = PlayStationSequenceEventReader.ReadToEndOfTrack(archive, track.EventsOffset, track.EndOffset);
        var cursor = new SequenceEventCursor(archive[track.EventsOffset..track.EndOffset]);
        var events = new List<SequenceMidiEvent>();
        int running = 0;
        while (!cursor.AtEnd)
        {
            int delta = cursor.Variable();
            int first = cursor.Read();
            events.Add(first < 0xF0 ? Channel(ref cursor, delta, first, ref running) : System(ref cursor, delta, first));
            if (first is >= 0xF0 and < 0xF8)
            {
                running = 0;
            }
        }

        return events;
    }

    /// <summary>Reads a channel message including its optional running status.</summary>
    /// <param name="cursor">Bounded input cursor.</param>
    /// <param name="delta">Original tick delta.</param>
    /// <param name="first">Status or running-status data byte.</param>
    /// <param name="running">Previous channel status.</param>
    /// <returns>Prepared channel event.</returns>
    private static SequenceMidiEvent Channel(ref SequenceEventCursor cursor, int delta, int first, ref int running)
    {
        int status = first < 128 ? running : first;
        int data1 = first < 128 ? first : cursor.Read();
        int data2 = (status & 0xF0) is 0xC0 or 0xD0 ? 0 : cursor.Read();
        running = status;
        return new SequenceMidiEvent(delta, status, data1, data2);
    }

    /// <summary>Reads metadata and retains other system statuses as explicit unsupported records.</summary>
    /// <param name="cursor">Bounded input cursor.</param>
    /// <param name="delta">Original tick delta.</param>
    /// <param name="status">System status.</param>
    /// <returns>Prepared metadata or unsupported-system record.</returns>
    private static SequenceMidiEvent System(ref SequenceEventCursor cursor, int delta, int status)
    {
        if (status == 0xFF)
        {
            return Meta(ref cursor, delta);
        }

        int length = status switch
        {
            0xF0 or 0xF7 => cursor.Variable(),
            0xF1 or 0xF3 => 1,
            0xF2 => 2,
            _ => 0,
        };
        cursor.Skip(length);
        return new SequenceMidiEvent(delta, status, 0, 0);
    }

    /// <summary>Reads SEP metadata, whose tempo payload has no MIDI length prefix.</summary>
    /// <param name="cursor">Bounded input cursor.</param>
    /// <param name="delta">Original tick delta.</param>
    /// <returns>Prepared tempo, end marker or explicitly identified opaque metadata.</returns>
    private static SequenceMidiEvent Meta(ref SequenceEventCursor cursor, int delta)
    {
        int type = cursor.Read();
        if (type == 0x51)
        {
            int tempo = (cursor.Read() << 16) | (cursor.Read() << 8) | cursor.Read();
            return new SequenceMidiEvent(delta, 0xFF, type, tempo);
        }

        cursor.Skip(cursor.Variable());
        return new SequenceMidiEvent(delta, 0xFF, type, 0);
    }
}
