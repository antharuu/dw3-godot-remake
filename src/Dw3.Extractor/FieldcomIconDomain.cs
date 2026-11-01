using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>The extracted FIELDCOM icon images and animation sequences.</summary>
public sealed record FieldcomIconDomain(string DiscPath, string Sha256, int ByteCount, IReadOnlyList<FieldcomExtractedFrame> Frames, IReadOnlyList<FieldcomAnimation> Animations);
