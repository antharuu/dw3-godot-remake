namespace Dw3.Formats.Binary;

/// <summary>
/// Decompresses the RLEN run-length encoding found in several disc files.
///
/// RLEN stream layout (all integers are little-endian):
/// <list type="bullet">
///   <item>bytes 0–3  : ASCII magic <c>RLEN</c></item>
///   <item>bytes 4–7  : expected decompressed size (uint32)</item>
///   <item>bytes 8+   : command stream terminated by a zero byte</item>
/// </list>
///
/// Command byte semantics:
/// <list type="bullet">
///   <item><c>0x00</c>       — end of stream; output must match the declared size exactly.</item>
///   <item><c>0x01–0x7F</c>  — literal run: copy the next N bytes verbatim to output.</item>
///   <item><c>0x81–0xFF</c>  — repeat run: repeat the next byte (N &amp; 0x7F) times.</item>
///   <item><c>0x80</c>       — treated as end of stream (repeat count 0 = noop, but terminate).</item>
/// </list>
/// </summary>
public static class RlenDecoder
{
    private static ReadOnlySpan<byte> Magic => "RLEN"u8;

    /// <summary>Returns true when the first four bytes of <paramref name="data"/> are the RLEN magic.</summary>
    public static bool IsRlen(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[..4].SequenceEqual(Magic);

    /// <summary>
    /// Decompresses an RLEN-encoded stream.
    /// Throws <see cref="FormatException"/> for any structural violation:
    /// bad magic, truncated header, overflowing run, missing terminator, or
    /// decompressed size mismatch.
    /// </summary>
    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (!IsRlen(data))
            throw new FormatException("RLEN magic not found at stream start");
        if (data.Length < 8)
            throw new FormatException(
                $"RLEN header is truncated ({data.Length} bytes; need at least 8)");

        uint target = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data[4..8]);
        var output = new byte[target];
        int outPos = 0;
        int pos = 8;

        while (true)
        {
            if (pos >= data.Length)
                throw new FormatException("RLEN stream has no terminator (0x00 end-of-stream byte)");

            byte command = data[pos++];

            // 0x00: end of stream (0x80 is treated the same: repeat of count 0)
            if (command == 0 || command == 0x80)
                break;

            if ((command & 0x80) != 0)
            {
                // Repeat run: replicate next byte (command & 0x7F) times
                int count = command & 0x7F;
                if (pos >= data.Length)
                    throw new FormatException(
                        $"RLEN repeat command at input offset 0x{pos - 1:X} has no value byte");
                byte value = data[pos++];
                if (outPos + count > target)
                    throw new FormatException(
                        $"RLEN repeat run at output offset 0x{outPos:X} would exceed declared size {target}");
                output.AsSpan(outPos, count).Fill(value);
                outPos += count;
            }
            else
            {
                // Literal run: copy next `command` bytes verbatim
                int count = command;
                if (pos + count > data.Length)
                    throw new FormatException(
                        $"RLEN literal run at input offset 0x{pos - 1:X} (count {count}) exceeds input buffer");
                if (outPos + count > target)
                    throw new FormatException(
                        $"RLEN literal run at output offset 0x{outPos:X} would exceed declared size {target}");
                data.Slice(pos, count).CopyTo(output.AsSpan(outPos));
                pos += count;
                outPos += count;
            }
        }

        if (outPos != (int)target)
            throw new FormatException(
                $"RLEN decompressed {outPos} bytes but declared size is {target}");

        return output;
    }

    /// <summary>
    /// Decompresses <paramref name="data"/> if it starts with the RLEN magic;
    /// otherwise returns the original data unchanged.
    /// </summary>
    /// <returns>
    /// <c>(decompressed, true)</c> when the input was RLEN-encoded;
    /// <c>(original copy, false)</c> otherwise.
    /// </returns>
    public static (byte[] Data, bool WasCompressed) DecompressIfNeeded(ReadOnlySpan<byte> data)
    {
        if (!IsRlen(data))
            return (data.ToArray(), false);
        return (Decompress(data), true);
    }
}
