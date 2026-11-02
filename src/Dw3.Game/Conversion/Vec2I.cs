namespace Dw3Production.Conversion;

/// <summary>Engine-free 2D integer coordinate. The Conversion records stay free of Godot types
/// so the standalone xUnit projects can compile them without a GodotSharp reference. The Godot
/// interop (implicit conversion to <c>Vector2</c>) lives in the Godot-only partial in Vec2I.Godot.cs.</summary>
public readonly partial record struct Vec2I(int X, int Y);
