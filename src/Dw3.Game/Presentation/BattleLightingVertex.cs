using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The lighting inputs kept for one emitted battle-model vertex, so a later stage light set can
/// recolour the vertex without re-reading the model JSON.
/// </summary>
/// <param name="Normal">Source normal already converted to Godot axes (Y up, -Z forward); only
/// meaningful when <paramref name="Lit"/> is true, otherwise <see cref="Vector3.Up"/>.</param>
/// <param name="Lit">True for a normal-lit primitive, false for a flat-coloured one.</param>
/// <param name="Flat">Baked colour as source RGB/255: the flat command colour, or the neutral
/// fallback light for a lit primitive until stage lights are bound.</param>
internal readonly record struct BattleLightingVertex(Vector3 Normal, bool Lit, Color Flat);
