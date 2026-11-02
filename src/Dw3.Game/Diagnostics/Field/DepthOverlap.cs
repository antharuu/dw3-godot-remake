using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>A player placement where an opaque player texel covers an opaque terrain texel of a different colour.</summary>
/// <param name="PlayerPosition">Player position (foot anchor) that puts the sprite over the terrain texel.</param>
/// <param name="WorldPixel">World coordinate of the overlapping texel.</param>
/// <param name="ExpectedForeground">Colour that must win at that texel: the player over the background layer, the terrain for the foreground layer.</param>
internal sealed record DepthOverlap(NumericsVector2 PlayerPosition, NumericsVector2 WorldPixel, Color ExpectedForeground);
