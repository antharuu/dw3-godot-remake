namespace Dw3.Formats.Battle;

/// <summary>Reads bounded, previously validated SEP event payloads for offline preparation.</summary>
internal ref struct SequenceEventCursor
{
    private readonly ReadOnlySpan<byte> _data;
    private int _offset;

    /// <summary>Initializes a new instance of the <see cref="SequenceEventCursor"/> struct.</summary>
    /// <param name="data">One bounded event stream.</param>
    public SequenceEventCursor(ReadOnlySpan<byte> data) => _data = data;

    /// <summary>Gets a value indicating whether the stream has been consumed.</summary>
    public readonly bool AtEnd => _offset == _data.Length;

    /// <summary>Reads one bounded byte.</summary>
    /// <returns>The next byte.</returns>
    public byte Read() => _offset < _data.Length ? _data[_offset++] : throw new FormatException("Truncated SEP event.");

    /// <summary>Reads a MIDI variable-length integer.</summary>
    /// <returns>Decoded integer.</returns>
    public int Variable()
    {
        int value = 0;
        for (int index = 0; index < 4; index++)
        {
            byte next = Read();
            value = (value << 7) | (next & 127);
            if (next < 128)
            {
                return value;
            }
        }

        throw new FormatException("SEP variable integer exceeds four bytes.");
    }

    /// <summary>Skips a bounded opaque payload without interpreting it.</summary>
    /// <param name="length">Number of bytes to skip.</param>
    public void Skip(int length)
    {
        if (length < 0 || length > _data.Length - _offset)
        {
            throw new FormatException("Truncated SEP payload.");
        }

        _offset += length;
    }
}
