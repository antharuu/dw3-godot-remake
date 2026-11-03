namespace Dw3Production.Conversion;

/// <summary>A climb-up (slot type 2) or climb-down (slot type 3) slot. X/Y are the source
/// slot pixel coordinates the actor is placed at when the climb starts; Height is the wall
/// height in source pixels ((arg - 1) * 16).</summary>
public sealed record Climb(int Slot, int Type, int X, int Y, int Height);
