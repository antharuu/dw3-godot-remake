namespace Dw3.Formats.Field;

/// <summary>One action animation table group with five directional sequence references.</summary>
public sealed record ActionAnimationSet(int SetId, IReadOnlyList<int> SequenceIndices);
