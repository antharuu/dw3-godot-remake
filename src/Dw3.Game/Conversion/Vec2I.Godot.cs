using Godot;

namespace Dw3Production.Conversion;

// Godot-only half of the engine-free Vec2I coordinate (see DialogueRecords.cs). This file is
// compiled by the Godot project only, never by the standalone xUnit projects, so the implicit
// conversion to Godot's Vector2 keeps the renderer's "boxOrigin + layout.SpritePos" arithmetic
// source-identical while the Conversion layer itself stays free of Godot types.
public readonly partial record struct Vec2I
{
    /// <summary>Converts the integer pixel coordinate to a Godot vector so renderers can add layout offsets to node positions directly. The conversion is exact for any value within ±2^24.</summary>
    /// <param name="v">Coordinate in pixels.</param>
    /// <returns>A vector with the same X and Y.</returns>
    public static implicit operator Vector2(Vec2I v) => new(v.X, v.Y);
}
