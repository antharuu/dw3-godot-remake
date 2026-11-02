namespace Dw3Production.Conversion;

/// <summary>The action animation sets of one player action archive, keyed by set id.</summary>
public sealed record ClimbData(IReadOnlyDictionary<int, ClimbSequence> Sequences);
