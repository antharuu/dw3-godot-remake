namespace Dw3.Formats.Field;

/// <summary>A terminated frame sequence recovered from a field overlay.</summary>
public sealed record FieldcomFrameSequence(IReadOnlyList<FieldcomFrameStep> Steps);
