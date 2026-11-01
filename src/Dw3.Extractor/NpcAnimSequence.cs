namespace Dw3.Extractor;

/// <summary>One decoded animation sequence for a character archive.</summary>
public sealed record NpcAnimSequence(
    int SequenceIndex,
    NpcSequenceTerminator Terminator,
    IReadOnlyList<NpcAnimRecord> Records);
