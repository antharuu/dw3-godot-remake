using System.Buffers.Binary;

namespace Dw3.Formats.Binary;

/// <summary>
/// Reads little-endian primitive values from a bounded byte buffer.
///
/// Every read validates that the requested range lies entirely within the buffer
/// and throws <see cref="FormatException"/> with an explicit offset message when
/// it does not.  There are no silent truncations or default returns.
/// </summary>
public sealed class BinarySpanReader
{
    private readonly byte[] _data;

    /// <param name="data">Source byte buffer. The reader does not copy it.</param>
    /// <exception cref="ArgumentNullException"/>
    public BinarySpanReader(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>Gets total number of bytes in the buffer.</summary>
    public int Length => _data.Length;

    /// <summary>Returns the underlying buffer as a read-only span.</summary>
    public ReadOnlySpan<byte> AsSpan() => _data;

    /// <summary>Reads a single unsigned byte.</summary>
    /// <exception cref="FormatException">Offset is out of range.</exception>
    public byte ReadU8(int offset)
    {
        if ((uint)offset >= (uint)_data.Length)
            throw new FormatException(
                $"u8 read at 0x{offset:X} is outside buffer (length {_data.Length})");
        return _data[offset];
    }

    /// <summary>Reads a little-endian unsigned 16-bit integer.</summary>
    /// <exception cref="FormatException">Read would exceed buffer bounds.</exception>
    public ushort ReadU16(int offset)
    {
        if (offset < 0 || offset + 2 > _data.Length)
            throw new FormatException(
                $"u16 read at 0x{offset:X} would exceed buffer (length {_data.Length})");
        return BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(offset, 2));
    }

    /// <summary>Reads a little-endian unsigned 32-bit integer.</summary>
    /// <exception cref="FormatException">Read would exceed buffer bounds.</exception>
    public uint ReadU32(int offset)
    {
        if (offset < 0 || offset + 4 > _data.Length)
            throw new FormatException(
                $"u32 read at 0x{offset:X} would exceed buffer (length {_data.Length})");
        return BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(offset, 4));
    }

    /// <summary>Reads a little-endian signed 32-bit integer.</summary>
    /// <exception cref="FormatException">Read would exceed buffer bounds.</exception>
    public int ReadI32(int offset)
    {
        if (offset < 0 || offset + 4 > _data.Length)
            throw new FormatException(
                $"i32 read at 0x{offset:X} would exceed buffer (length {_data.Length})");
        return BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(offset, 4));
    }

    /// <summary>Reads a little-endian signed 16-bit integer.</summary>
    /// <exception cref="FormatException">Read would exceed buffer bounds.</exception>
    public short ReadI16(int offset)
    {
        if (offset < 0 || offset + 2 > _data.Length)
            throw new FormatException(
                $"i16 read at 0x{offset:X} would exceed buffer (length {_data.Length})");
        return BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(offset, 2));
    }

    /// <summary>
    /// Returns a copy of <paramref name="length"/> bytes starting at <paramref name="offset"/>.
    /// </summary>
    /// <exception cref="FormatException">Slice range is out of bounds.</exception>
    public byte[] Slice(int offset, int length)
    {
        if (offset < 0 || length < 0 || (long)offset + length > _data.Length)
            throw new FormatException(
                $"slice [0x{offset:X}..0x{offset + length:X}) is outside buffer (length {_data.Length})");
        return _data.AsSpan(offset, length).ToArray();
    }
}
