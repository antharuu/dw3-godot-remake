namespace Dw3.Extractor;

/// <summary>One animation set with five directional sequence references.</summary>
public sealed record NpcAnimSet(int SetId, IReadOnlyList<int> SequenceIndices);
