namespace Dw3.Formats.Field;

/// <summary>One frame and duration pair recovered from field overlay data.</summary>
public sealed record FieldcomFrameStep(int Frame, int Duration);
