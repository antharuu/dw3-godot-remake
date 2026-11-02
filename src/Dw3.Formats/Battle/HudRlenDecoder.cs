using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// Decompresses the RLEN blocks of the battle HUD and menu texture files.
/// </summary>
/// <remarks>
/// Stream: <c>"RLEN"</c>, u32 decompressed size, then commands. A command byte of 0x00 ends the
/// stream. A byte with bit 7 set repeats the following value byte <c>cmd &amp; 0x7F</c> times, so
/// 0x80 is a zero-count repeat that still consumes its value byte. Any other byte copies that many
/// literal bytes. Unlike <see cref="Binary.RlenDecoder"/>, only 0x00 terminates the stream.
/// </remarks>
public static class HudRlenDecoder
{
    private const int HeaderBytes = 8;

    /// <summary>
    /// Decompresses an RLEN block following the battle_hud.py _rlen semantics exactly:
    /// only 0x00 is end-of-stream; 0x80 is a 0-count repeat (reads and discards value byte).
    /// </summary>
    /// <param name="data">RLEN-compressed bytes starting with the 'RLEN' magic and 4-byte target size.</param>
    /// <returns>The decompressed output, exactly target-size bytes.</returns>
    /// <exception cref="FormatException">The header is missing the RLEN magic or the decompressed data is truncated.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (data.Length < 9 || data[0] != 'R' || data[1] != 'L' || data[2] != 'E' || data[3] != 'N')
        {
            throw new FormatException("Expected RLEN-compressed data.");
        }

        uint target = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));
        var output = new byte[target];
        int pos = HeaderBytes;
        int outPos = 0;
        bool more = true;
        while (more)
        {
            more = ReadCommand(data, ref pos, output, ref outPos);
        }

        if (outPos != (int)target)
        {
            throw new FormatException($"RLEN decompressed {outPos} bytes, expected {target}.");
        }

        return output;
    }

    /// <summary>Decodes the command at <paramref name="pos"/> and appends its bytes to <paramref name="output"/>.</summary>
    /// <param name="data">Whole RLEN block.</param>
    /// <param name="pos">Input offset of the command byte; advanced past its payload.</param>
    /// <param name="output">Buffer sized to the declared decompressed length.</param>
    /// <param name="outPos">Number of bytes already written; advanced by the bytes produced.</param>
    /// <returns><see langword="false"/> when the command is the 0x00 end marker.</returns>
    /// <exception cref="FormatException">The stream ends without a marker, a payload is truncated or the output overflows.</exception>
    private static bool ReadCommand(ReadOnlySpan<byte> data, ref int pos, byte[] output, ref int outPos)
    {
        if (pos >= data.Length)
        {
            throw new FormatException("RLEN stream lacks end marker.");
        }

        byte cmd = data[pos++];
        if (cmd == 0)
        {
            return false;
        }

        if ((cmd & 0x80) != 0)
        {
            WriteRepeat(data, ref pos, cmd & 0x7F, output, ref outPos);
        }
        else
        {
            WriteLiteral(data, ref pos, cmd, output, ref outPos);
        }

        return true;
    }

    /// <summary>Writes <paramref name="count"/> copies of the value byte at <paramref name="pos"/>.</summary>
    /// <param name="data">Whole RLEN block.</param>
    /// <param name="pos">Input offset of the value byte; advanced past it.</param>
    /// <param name="count">Repeat count, 0..127.</param>
    /// <param name="output">Decompressed buffer.</param>
    /// <param name="outPos">Output write offset; advanced by <paramref name="count"/>.</param>
    /// <exception cref="FormatException">The value byte is missing or the run overflows the declared size.</exception>
    private static void WriteRepeat(ReadOnlySpan<byte> data, ref int pos, int count, byte[] output, ref int outPos)
    {
        if (pos >= data.Length)
        {
            throw new FormatException("RLEN repeat command has no value byte.");
        }

        byte val = data[pos++];
        if (outPos + count > output.Length)
        {
            throw new FormatException("RLEN exceeds declared size.");
        }

        output.AsSpan(outPos, count).Fill(val);
        outPos += count;
    }

    /// <summary>Copies <paramref name="count"/> literal bytes from <paramref name="pos"/>.</summary>
    /// <param name="data">Whole RLEN block.</param>
    /// <param name="pos">Input offset of the first literal byte; advanced past the run.</param>
    /// <param name="count">Literal run length, 1..127.</param>
    /// <param name="output">Decompressed buffer.</param>
    /// <param name="outPos">Output write offset; advanced by <paramref name="count"/>.</param>
    /// <exception cref="FormatException">The run exceeds the input or overflows the declared size.</exception>
    private static void WriteLiteral(ReadOnlySpan<byte> data, ref int pos, int count, byte[] output, ref int outPos)
    {
        if (pos + count > data.Length)
        {
            throw new FormatException("RLEN literal run exceeds input.");
        }

        if (outPos + count > output.Length)
        {
            throw new FormatException("RLEN exceeds declared size.");
        }

        data.Slice(pos, count).CopyTo(output.AsSpan(outPos));
        pos += count;
        outPos += count;
    }
}
