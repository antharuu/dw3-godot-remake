using System.Buffers.Binary;

namespace Dw3.Formats.Text;

/// <summary>
/// Decodes EU French field text tables (FSTALK{NN}.BIN).
///
/// File layout (FSTALKNN.BIN binary structure):
///   s32 count, then count×u32 byte offsets from the file start.
///   Entry i is the null-terminated string at offsets[i].
///
/// Tokenisation (non-SJIS mode, EU French edition):
///   0x00              string terminator (not a token)
///   0x01 {index}      icon
///   0x02 {cmd} {args} control code; argument count from FONT_CODE_LENGTHS[cmd]
///   0x03              reserved — rejected as an error
///   0x04..0xFF        glyph
///
/// FONT_CODE_LENGTHS[0..9]: { 1, 2, 3, 2, 5, 3, 3, 2, 3, 2 }.
/// Command ≥ 10 is unknown and rejected rather than silently skipped.
/// </summary>
public static class TextTableDecoder
{
    // FONT_CODE_LENGTHS[cmd] = total bytes consumed starting at 0x02 (includes the 0x02 byte itself).
    private static readonly int[] ControlLengths = [1, 2, 3, 2, 5, 3, 3, 2, 3, 2];

    /// <summary>Maximum valid control command (inclusive).</summary>
    public static int MaxControl => ControlLengths.Length - 1;

    /// <summary>
    /// Decodes the complete entry table from one FSTALKNN.BIN file.
    /// </summary>
    /// <param name="data">Full file bytes.</param>
    /// <returns>One token list per table entry (entry 0 = index 0 in the FieldTalk stream).</returns>
    /// <exception cref="FormatException">Any structural or tokenisation violation.</exception>
    public static IReadOnlyList<IReadOnlyList<TextToken>> DecodeFile(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            throw new FormatException("Text table too short to read entry count.");
        uint rawCount = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (rawCount > 0x10000)
            throw new FormatException($"Text table entry count {rawCount} is implausibly large.");
        int count = (int)rawCount;
        int headerSize = 4 + count * 4;
        if (headerSize > data.Length)
        {
            throw new FormatException($"Text table offset header is truncated (needs {headerSize} bytes, got {data.Length}).");
        }

        var offsets = new int[count];
        for (int i = 0; i < count; i++)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data[(4 + i * 4)..]);
            if (offset < (uint)headerSize || offset >= (uint)data.Length)
                throw new FormatException($"Text table entry {i} offset 0x{offset:X} is outside the string payload.");
            offsets[i] = (int)offset;
        }

        // Offsets must be monotonically non-decreasing.
        for (int i = 1; i < count; i++)
            if (offsets[i] < offsets[i - 1])
            {
                throw new FormatException($"Text table offsets are not monotonic at index {i}.");
            }

        var entries = new IReadOnlyList<TextToken>[count];
        for (int i = 0; i < count; i++)
        {
            int start = offsets[i];
            int nextBound = i + 1 < count ? offsets[i + 1] : data.Length;
            int terminator = IndexOfZero(data, start, nextBound);
            if (terminator < 0)
                throw new FormatException($"Text table entry {i} has no null terminator within its bounds.");
            entries[i] = Tokenize(data, start, terminator);
        }
        return entries;
    }

    /// <summary>
    /// Tokenises one null-terminated string in <paramref name="data"/>[<paramref name="start"/>..
    /// <paramref name="end"/>).
    /// </summary>
    /// <exception cref="FormatException">Unknown control command, truncated control or icon, or reserved byte 0x03.</exception>
    public static IReadOnlyList<TextToken> Tokenize(ReadOnlySpan<byte> data, int start, int end)
    {
        var tokens = new List<TextToken>();
        int pos = start;
        while (pos < end)
        {
            byte b = data[pos];
            if (b == 0x02)
            {
                // Control: 0x02 {cmd} {args...}
                if (pos + 1 >= end)
                    throw new FormatException($"Truncated control id at offset 0x{pos:X}.");
                int cmd = data[pos + 1];
                if (cmd > MaxControl)
                    throw new FormatException($"Unknown control command 0x{cmd:X2} at offset 0x{pos:X}.");
                int length = ControlLengths[cmd];
                if (pos + length > end)
                    throw new FormatException($"Truncated control 0x{cmd:X2} at offset 0x{pos:X} (needs {length} bytes).");
                var args = new int[length - 2]; // length includes 0x02 and cmd
                for (int a = 0; a < args.Length; a++)
                    args[a] = data[pos + 2 + a];
                tokens.Add(new TextToken(TextTokenKind.Control, cmd, args));
                pos += length;
            }
            else if (b == 0x01)
            {
                // Icon: 0x01 {index}
                if (pos + 1 >= end)
                    throw new FormatException($"Truncated icon at offset 0x{pos:X}.");
                tokens.Add(new TextToken(TextTokenKind.Icon, data[pos + 1], []));
                pos += 2;
            }
            else if (b == 0x03)
            {
                throw new FormatException($"Reserved font code 0x03 at offset 0x{pos:X}.");
            }
            else
            {
                // Glyph (0x04..0xFF).
                tokens.Add(new TextToken(TextTokenKind.Glyph, b, []));
                pos++;
            }
        }
        return tokens;
    }

    private static int IndexOfZero(ReadOnlySpan<byte> data, int start, int limit)
    {
        for (int i = start; i < limit; i++)
            if (data[i] == 0)
                return i;
        return -1;
    }
}
