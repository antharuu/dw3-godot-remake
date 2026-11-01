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
        {
            throw new FormatException("RLEN magic not found at stream start");
        }

        if (data.Length < 8)
        {
            throw new FormatException(
                $"RLEN header is truncated ({data.Length} bytes; need at least 8)");
        }

        uint target = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data[4..8]);
        var output = new byte[target];
        int outPos = 0;
        int pos = 8;

        while (true)
        {
            byte command = ReadCommand(data, ref pos);
            if (command == 0 || command == 0x80)
            {
                break;
            }

            if ((command & 0x80) != 0)
            {
                DecodeRepeatRun(data, output, ref pos, ref outPos, command, target);
            }
            else
            {
                DecodeLiteralRun(data, output, ref pos, ref outPos, command, target);
            }
        }

        ValidateOutputSize(outPos, target);
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
        {
            return (data.ToArray(), false);
        }

        return (Decompress(data), true);
    }

    /// <summary>Reads the next command byte and rejects a stream that ends before a terminator.</summary>
    /// <param name="data">Entire encoded stream.</param>
    /// <param name="position">Input cursor advanced past the returned command.</param>
    /// <returns>The command byte at the prior cursor.</returns>
    private static byte ReadCommand(ReadOnlySpan<byte> data, ref int position)
    {
        if (position >= data.Length)
        {
            throw new FormatException("RLEN stream has no terminator (0x00 end-of-stream byte)");
        }

        byte command = data[position];
        position++;
        return command;
    }

    /// <summary>Expands one repeat command, including its following byte, into the output buffer.</summary>
    /// <param name="data">Entire encoded stream.</param>
    /// <param name="output">Destination allocation sized from the header.</param>
    /// <param name="inputPosition">Input cursor at the repeated value byte.</param>
    /// <param name="outputPosition">Output cursor to advance by the repeat count.</param>
    /// <param name="command">Repeat command whose low seven bits are the count.</param>
    /// <param name="target">Declared decompressed byte count.</param>
    private static void DecodeRepeatRun(
        ReadOnlySpan<byte> data,
        byte[] output,
        ref int inputPosition,
        ref int outputPosition,
        byte command,
        uint target)
    {
        int count = command & 0x7F;
        if (inputPosition >= data.Length)
        {
            throw new FormatException(
                $"RLEN repeat command at input offset 0x{inputPosition - 1:X} has no value byte");
        }

        byte value = data[inputPosition];
        inputPosition++;
        EnsureOutputCapacity(outputPosition, count, target, "repeat");
        output.AsSpan(outputPosition, count).Fill(value);
        outputPosition += count;
    }

    /// <summary>Copies one literal command's payload from the input cursor into the output cursor.</summary>
    /// <param name="data">Entire encoded stream.</param>
    /// <param name="output">Destination allocation sized from the header.</param>
    /// <param name="inputPosition">Input cursor at the literal payload.</param>
    /// <param name="outputPosition">Output cursor to advance by the literal count.</param>
    /// <param name="command">Literal command whose value is the byte count.</param>
    /// <param name="target">Declared decompressed byte count.</param>
    private static void DecodeLiteralRun(
        ReadOnlySpan<byte> data,
        byte[] output,
        ref int inputPosition,
        ref int outputPosition,
        byte command,
        uint target)
    {
        int count = command;
        if (inputPosition + count > data.Length)
        {
            throw new FormatException(
                $"RLEN literal run at input offset 0x{inputPosition - 1:X} (count {count}) exceeds input buffer");
        }

        EnsureOutputCapacity(outputPosition, count, target, "literal");
        data.Slice(inputPosition, count).CopyTo(output.AsSpan(outputPosition));
        inputPosition += count;
        outputPosition += count;
    }

    /// <summary>Rejects a command whose decoded bytes would exceed the header's declared output size.</summary>
    /// <param name="outputPosition">First destination byte for the command.</param>
    /// <param name="count">Number of bytes the command writes.</param>
    /// <param name="target">Declared decompressed byte count.</param>
    /// <param name="kind">Human-readable command category for the error.</param>
    private static void EnsureOutputCapacity(int outputPosition, int count, uint target, string kind)
    {
        if (outputPosition + count > target)
        {
            throw new FormatException(
                $"RLEN {kind} run at output offset 0x{outputPosition:X} would exceed declared size {target}");
        }
    }

    /// <summary>Checks that the terminating command arrived exactly at the declared output size.</summary>
    /// <param name="outputPosition">Number of bytes decoded before the terminator.</param>
    /// <param name="target">Declared decompressed byte count.</param>
    private static void ValidateOutputSize(int outputPosition, uint target)
    {
        if (outputPosition != (int)target)
        {
            throw new FormatException(
                $"RLEN decompressed {outputPosition} bytes but declared size is {target}");
        }
    }
}
