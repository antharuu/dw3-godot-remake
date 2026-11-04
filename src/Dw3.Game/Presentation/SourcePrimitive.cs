namespace Dw3Production.Presentation;

/// <summary>Stores one source primitive's ordering key and unsplit corner count.</summary>
/// <param name="Key">Ordering-table depth bucket selected from the posed source vertices.</param>
/// <param name="CornerCount">Three for a triangle or four for a quad.</param>
public readonly record struct SourcePrimitive(int Key, int CornerCount);
