namespace Dw3.Formats.Text;

/// <summary>
/// Converts one bounded null-terminated field-text entry into glyph, icon, and control tokens.
/// </summary>
internal static class TextEntryTokenizer
{
    private static readonly int[] ControlLengths = [1, 2, 3, 2, 5, 3, 3, 2, 3, 2];

    /// <summary>Gets the highest control-command byte understood by this tokenizer.</summary>
    public static int MaxControl => ControlLengths.Length - 1;

    /// <summary>
    /// Tokenizes bytes in the bounded entry range.
    /// </summary>
    /// <param name="data">Complete source file bytes.</param>
    /// <param name="start">Inclusive first entry byte.</param>
    /// <param name="end">Exclusive null-terminator position.</param>
    /// <returns>The tokens represented by the bounded entry.</returns>
    /// <exception cref="FormatException">A sequence is reserved, unknown, or truncated.</exception>
    public static IReadOnlyList<TextToken> Tokenize(ReadOnlySpan<byte> data, int start, int end)
    {
        var tokens = new List<TextToken>();
        int position = start;
        while (position < end)
        {
            position = ReadToken(data, end, position, tokens);
        }

        return tokens;
    }

    /// <summary>Reads one token and advances the byte position.</summary>
    /// <param name="data">Complete source file bytes.</param>
    /// <param name="end">Exclusive null-terminator position.</param>
    /// <param name="position">Current entry position.</param>
    /// <param name="tokens">Destination token collection.</param>
    /// <returns>The first unread byte position.</returns>
    private static int ReadToken(ReadOnlySpan<byte> data, int end, int position, ICollection<TextToken> tokens)
    {
        return data[position] switch
        {
            0x01 => ReadIcon(data, end, position, tokens),
            0x02 => ReadControl(data, end, position, tokens),
            0x03 => ThrowReservedCode(position),
            _ => ReadGlyph(data[position], position, tokens),
        };
    }

    /// <summary>Reads an inline icon token.</summary>
    /// <param name="data">Complete source file bytes.</param>
    /// <param name="end">Exclusive null-terminator position.</param>
    /// <param name="position">Icon marker position.</param>
    /// <param name="tokens">Destination token collection.</param>
    /// <returns>The first unread byte position.</returns>
    private static int ReadIcon(ReadOnlySpan<byte> data, int end, int position, ICollection<TextToken> tokens)
    {
        if (position + 1 >= end)
        {
            throw new FormatException($"Truncated icon at offset 0x{position:X}.");
        }

        tokens.Add(new TextToken(TextTokenKind.Icon, data[position + 1], []));
        return position + 2;
    }

    /// <summary>Reads a bounded control token.</summary>
    /// <param name="data">Complete source file bytes.</param>
    /// <param name="end">Exclusive null-terminator position.</param>
    /// <param name="position">Control marker position.</param>
    /// <param name="tokens">Destination token collection.</param>
    /// <returns>The first unread byte position.</returns>
    private static int ReadControl(ReadOnlySpan<byte> data, int end, int position, ICollection<TextToken> tokens)
    {
        int command = ReadControlCommand(data, end, position);
        int length = ControlLengths[command];
        EnsureControlFits(end, position, command, length);
        tokens.Add(new TextToken(TextTokenKind.Control, command, ReadArguments(data, position, length)));
        return position + length;
    }

    /// <summary>Reads and validates the control-command byte.</summary>
    /// <param name="data">Complete source file bytes.</param>
    /// <param name="end">Exclusive null-terminator position.</param>
    /// <param name="position">Control marker position.</param>
    /// <returns>The validated control-command byte.</returns>
    private static int ReadControlCommand(ReadOnlySpan<byte> data, int end, int position)
    {
        if (position + 1 >= end)
        {
            throw new FormatException($"Truncated control id at offset 0x{position:X}.");
        }

        int command = data[position + 1];
        if (command > MaxControl)
        {
            throw new FormatException($"Unknown control command 0x{command:X2} at offset 0x{position:X}.");
        }

        return command;
    }

    /// <summary>Ensures that a complete control sequence remains in bounds.</summary>
    /// <param name="end">Exclusive null-terminator position.</param>
    /// <param name="position">Control marker position.</param>
    /// <param name="command">Validated control-command byte.</param>
    /// <param name="length">Total encoded control length.</param>
    private static void EnsureControlFits(int end, int position, int command, int length)
    {
        if (position + length > end)
        {
            throw new FormatException($"Truncated control 0x{command:X2} at offset 0x{position:X} (needs {length} bytes).");
        }
    }

    /// <summary>Copies a control sequence's payload bytes.</summary>
    /// <param name="data">Complete source file bytes.</param>
    /// <param name="position">Control marker position.</param>
    /// <param name="length">Total encoded control length.</param>
    /// <returns>The control argument bytes.</returns>
    private static int[] ReadArguments(ReadOnlySpan<byte> data, int position, int length)
    {
        var arguments = new int[length - 2];
        for (int index = 0; index < arguments.Length; index++)
        {
            arguments[index] = data[position + 2 + index];
        }

        return arguments;
    }

    /// <summary>Throws the reserved-code violation.</summary>
    /// <param name="position">Reserved-code position.</param>
    /// <returns>This method never returns.</returns>
    private static int ThrowReservedCode(int position) =>
        throw new FormatException($"Reserved font code 0x03 at offset 0x{position:X}.");

    /// <summary>Appends a printable glyph token.</summary>
    /// <param name="value">Glyph code.</param>
    /// <param name="position">Glyph position.</param>
    /// <param name="tokens">Destination token collection.</param>
    /// <returns>The first unread byte position.</returns>
    private static int ReadGlyph(byte value, int position, ICollection<TextToken> tokens)
    {
        tokens.Add(new TextToken(TextTokenKind.Glyph, value, []));
        return position + 1;
    }
}
