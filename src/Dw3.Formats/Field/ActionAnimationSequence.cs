namespace Dw3.Formats.Field;

/// <summary>One decoded action animation sequence.</summary>
public sealed record ActionAnimationSequence(IReadOnlyList<ActionAnimationRecord> Records, ActionSequenceTerminator Terminator);
