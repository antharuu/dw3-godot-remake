namespace Dw3Production.Conversion;

/// <summary>One depth-sorted terrain layer of a field zone: the PNG image and the depth value
/// that controls sort order relative to the player and map objects.</summary>
public sealed record Layer(string Path, int SourceDepth);
