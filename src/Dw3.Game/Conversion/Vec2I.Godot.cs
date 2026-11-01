using Godot;

namespace Dw3Production.Conversion;

// Godot-only half of the engine-free Vec2I coordinate (see DialogueRecords.cs). This file is
// compiled by the Godot project only, never by the standalone xUnit projects, so the implicit
// conversion to Godot's Vector2 keeps the renderer's "boxOrigin + layout.SpritePos" arithmetic
// source-identical while the Conversion layer itself stays free of Godot types.
public readonly partial record struct Vec2I
{
    public static implicit operator Vector2(Vec2I v) => new(v.X, v.Y);
}
