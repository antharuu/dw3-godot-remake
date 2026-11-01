namespace Dw3.Formats.Field;

/// <summary>Decoded NPC lookup and progress-condition tables from the disc executable and FIELDSTG.PRO.</summary>
public sealed record NpcDiscTables(
    IReadOnlyList<NpcFileEntry?> FileEntries,
    IReadOnlyList<int> Widths,
    IReadOnlyList<NpcProgressRange> ProgressRanges,
    IReadOnlyDictionary<int, int> SpecialProgressRanges);
