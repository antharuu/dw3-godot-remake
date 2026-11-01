namespace Dw3.Formats.Field;

/// <summary>
/// Progress metadata read from the executable: the progress-range pairs and the special-condition
/// records whose check family selects a progress range.
/// </summary>
/// <param name="Ranges">Every {u8 min, u8 max} progress-range pair.</param>
/// <param name="SpecialRecordCount">Number of 3-byte special-condition records read, terminator included.</param>
/// <param name="SpecialProgressRanges">Special condition id to progress-range index, for range-check records only.</param>
public sealed record ProgressConditionTables(
    IReadOnlyList<NpcProgressRange> Ranges,
    int SpecialRecordCount,
    IReadOnlyDictionary<int, int> SpecialProgressRanges);

/// <summary>
/// Decodes the progress-range table and the special-condition table of the disc executable.
/// </summary>
/// <remarks>
/// Special-condition records are packed <c>{u8 id, u8 check, u8 argument}</c> triples ended by
/// <c>id == 0xFF</c>. A record whose check high nibble is <c>0x3</c> tests a progress range and its
/// argument indexes the range table. Other check families are kept out of the mapping.
/// </remarks>
public static class ProgressConditionDecoder
{
    private const int SpecialRecordBytes = 3;
    private const byte SpecialTerminator = 0xFF;
    private const int RangeCheckFamily = 0x30;

    /// <param name="executable">Raw executable bytes.</param>
    /// <param name="progressRangesOffset">File offset of the first range pair.</param>
    /// <param name="progressRangeCount">Number of range pairs.</param>
    /// <param name="specialConditionsOffset">File offset of the first special-condition record.</param>
    /// <param name="specialConditionsLimit">File offset the special records must terminate before.</param>
    /// <exception cref="FormatException">A table is outside the executable, unterminated or references a missing range.</exception>
    public static ProgressConditionTables Decode(
        ReadOnlySpan<byte> executable,
        int progressRangesOffset,
        int progressRangeCount,
        int specialConditionsOffset,
        int specialConditionsLimit)
    {
        if (progressRangesOffset < 0 || progressRangeCount < 0 || (long)progressRangesOffset + progressRangeCount * 2L > executable.Length)
            throw new FormatException("progress-range table is outside its disc member.");
        if (specialConditionsOffset < 0 || specialConditionsLimit > executable.Length || specialConditionsOffset >= specialConditionsLimit)
            throw new FormatException("Special-condition table range is outside the executable.");

        var ranges = new NpcProgressRange[progressRangeCount];
        for (int index = 0; index < ranges.Length; index++)
            ranges[index] = new NpcProgressRange(executable[progressRangesOffset + index * 2], executable[progressRangesOffset + index * 2 + 1]);

        var special = new Dictionary<int, int>();
        int records = 0;
        for (int offset = specialConditionsOffset; offset + SpecialRecordBytes <= specialConditionsLimit; offset += SpecialRecordBytes)
        {
            records++;
            int id = executable[offset];
            if (id == SpecialTerminator)
                return new ProgressConditionTables(ranges, records, special);
            int check = executable[offset + 1];
            int argument = executable[offset + 2];
            if ((check & 0xF0) != RangeCheckFamily)
                continue;
            if (argument >= ranges.Length)
                throw new FormatException($"Special condition {id} references progress range {argument}, outside {ranges.Length} records.");
            special[id] = argument;
        }
        throw new FormatException("Special-condition table has no 0xFF terminator.");
    }
}
