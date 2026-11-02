namespace Dw3Production.Conversion;

/// <summary>One action animation set. <see cref="Loops"/> is false for the one-shot sets.</summary>
public sealed record ClimbSequence(int Set, bool Loops, IReadOnlyList<ClimbRecord> Records);
