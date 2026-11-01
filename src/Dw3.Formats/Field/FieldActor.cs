namespace Dw3.Formats.Field;

/// <summary>Decoded actor position and width for occupancy checking.</summary>
public sealed record FieldActor(short X, short Y, int Width);
