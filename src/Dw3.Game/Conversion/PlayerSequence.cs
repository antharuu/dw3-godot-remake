namespace Dw3Production.Conversion;

/// <summary>One player animation sequence: the set index, the facing direction and the frame records.</summary>
public sealed record PlayerSequence(int Set, int Direction, IReadOnlyList<AnimationRecord> Records);
