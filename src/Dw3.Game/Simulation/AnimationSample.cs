namespace Dw3Production.Simulation;

/// <summary>
/// One sampled frame from a player animation sequence: the frame index, the sprite offsets
/// (source sprite sheet pixel offsets from the actor anchor), the foot-to-ground contacts
/// (for shadow placement), the resolved source direction (0=down CCW), and whether the
/// sprite should be flipped horizontally (for the four mirrored directions 4..7).
/// </summary>
public readonly record struct AnimationSample(int Frame, int OffsetX, int OffsetY, int FootGroundX, int FootGroundY, int SourceDirection, bool FlipH);
