namespace Dw3.Formats.Field;

/// <summary>Two-dimensional FIELDCOM VRAM pages, each 64 PS1 words by 256 rows.</summary>
public sealed record FieldcomVram(IReadOnlyList<ushort[]> Pages);
