namespace Dw3.Formats.Battle;

/// <summary>
/// One keyframe in a bone animation track, decoded from an 8-byte entry (4 × s16: x, y, z, frame)
/// inside a model's key archive. Translation tracks use world-scale integers; rotation and scale
/// tracks use fixed-point values whose unit depends on the consuming decoder.
/// </summary>
/// <param name="Frame">Frame index at which this key is sampled; frames increase monotonically within a track.</param>
/// <param name="X">X component of the transform value at this frame.</param>
/// <param name="Y">Y component of the transform value at this frame.</param>
/// <param name="Z">Z component of the transform value at this frame.</param>
public sealed record BattleTrackKey(int Frame, int X, int Y, int Z);
