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
    /// <summary>Gets maximum valid control command (inclusive).</summary>
    public static int MaxControl => TextEntryTokenizer.MaxControl;

    /// <summary>
    /// Decodes the complete entry table from one FSTALKNN.BIN file.
    /// </summary>
    /// <param name="data">Full file bytes.</param>
    /// <returns>One token list per table entry (entry 0 = index 0 in the FieldTalk stream).</returns>
    /// <exception cref="FormatException">Any structural or tokenisation violation.</exception>
    public static IReadOnlyList<IReadOnlyList<TextToken>> DecodeFile(ReadOnlySpan<byte> data)
    {
        int[] offsets = TextTableHeaderReader.ReadOffsets(data);
        var entries = new IReadOnlyList<TextToken>[offsets.Length];
        for (int index = 0; index < offsets.Length; index++)
        {
            int start = offsets[index];
            int bound = GetEntryBound(offsets, data.Length, index);
            entries[index] = DecodeEntry(data, start, bound, index);
        }

        return entries;
    }

    /// <summary>
    /// Tokenises one null-terminated string in <paramref name="data"/>[<paramref name="start"/>..
    /// <paramref name="end"/>).
    /// </summary>
    /// <param name="data">Full FSTALK file bytes containing the bounded string.</param>
    /// <param name="start">Inclusive byte offset of the first token.</param>
    /// <param name="end">Exclusive byte offset of the null terminator.</param>
    /// <returns>Tokens in byte-stream order, excluding the terminator.</returns>
    /// <exception cref="FormatException">Unknown control command, truncated control or icon, or reserved byte 0x03.</exception>
    public static IReadOnlyList<TextToken> Tokenize(ReadOnlySpan<byte> data, int start, int end) =>
        TextEntryTokenizer.Tokenize(data, start, end);

    /// <summary>Gets the exclusive bound for one offset-table entry.</summary>
    /// <param name="offsets">Validated offsets in table order.</param>
    /// <param name="dataLength">Complete file length.</param>
    /// <param name="index">Zero-based entry index.</param>
    /// <returns>The exclusive entry bound.</returns>
    private static int GetEntryBound(int[] offsets, int dataLength, int index) =>
        index + 1 < offsets.Length ? offsets[index + 1] : dataLength;

    /// <summary>Finds the terminator and tokenizes one bounded entry.</summary>
    /// <param name="data">Complete source file bytes.</param>
    /// <param name="start">Inclusive first entry byte.</param>
    /// <param name="bound">Exclusive entry bound.</param>
    /// <param name="index">Zero-based entry index.</param>
    /// <returns>The entry's token list.</returns>
    private static IReadOnlyList<TextToken> DecodeEntry(ReadOnlySpan<byte> data, int start, int bound, int index)
    {
        int terminator = IndexOfZero(data, start, bound);
        if (terminator < 0)
        {
            throw new FormatException($"Text table entry {index} has no null terminator within its bounds.");
        }

        return Tokenize(data, start, terminator);
    }

    /// <summary>Finds a null terminator inside a bounded range.</summary>
    /// <param name="data">Complete source file bytes.</param>
    /// <param name="start">Inclusive search start.</param>
    /// <param name="limit">Exclusive search limit.</param>
    /// <returns>The terminator position, or negative one when absent.</returns>
    private static int IndexOfZero(ReadOnlySpan<byte> data, int start, int limit)
    {
        for (int index = start; index < limit; index++)
        {
            if (data[index] == 0)
            {
                return index;
            }
        }

        return -1;
    }
}
