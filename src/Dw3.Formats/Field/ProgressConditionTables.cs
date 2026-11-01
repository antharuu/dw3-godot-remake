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
