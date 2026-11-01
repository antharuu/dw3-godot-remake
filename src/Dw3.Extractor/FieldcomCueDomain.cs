namespace Dw3.Extractor;

/// <summary>The extracted FIELDCOM cue images, exit rows, and named sequences.</summary>
public sealed record FieldcomCueDomain(string DiscPath, string Sha256, int ByteCount, IReadOnlyList<FieldcomExtractedFrame> Frames, IReadOnlyList<FieldcomExitRow> ExitRows, IReadOnlyList<FieldcomNamedSequence> InnerSequences, IReadOnlyList<FieldcomNamedSequence> PopSequences);
