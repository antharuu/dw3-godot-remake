namespace Dw3.Formats.Field;

/// <summary>Byte offsets into the disc executable for the progress-range and special-condition tables.</summary>
/// <param name="ProgressRangesOffset">File offset of the first progress-range pair.</param>
/// <param name="ProgressRangeCount">Number of progress-range pairs.</param>
/// <param name="SpecialConditionsOffset">File offset of the first special-condition record.</param>
/// <param name="SpecialConditionsLimit">File offset the special-condition records must terminate before.</param>
public sealed record NpcProgressTableLocator(
    int ProgressRangesOffset,
    int ProgressRangeCount,
    int SpecialConditionsOffset,
    int SpecialConditionsLimit);
