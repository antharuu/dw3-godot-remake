using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>A named FIELDCOM frame sequence from the cue or popup table.</summary>
public sealed record FieldcomNamedSequence(string Name, IReadOnlyList<FieldcomFrameStep> Steps);
