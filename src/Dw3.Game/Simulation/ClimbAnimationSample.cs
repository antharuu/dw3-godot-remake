namespace Dw3Production.Simulation;

/// <summary>
/// One sampled frame from a climb animation sequence: the frame index and the sprite
/// offsets (source sprite sheet pixel offsets from the actor anchor). Used by
/// <see cref="ClimbAnimation.Sample"/> and consumed by the climb presentation layer.
/// </summary>
public readonly record struct ClimbAnimationSample(int Frame, int OffsetX, int OffsetY);
