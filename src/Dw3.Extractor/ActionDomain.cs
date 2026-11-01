namespace Dw3.Extractor;

/// <summary>Disc-derived action archive metadata and decoded output for one domain.</summary>
public sealed record ActionDomain(
    string Name,
    string ManifestSchema,
    int FileId,
    int AnimationSetEntry,
    string DiscPath,
    string Sha256,
    int ByteCount,
    IReadOnlyList<ActionFrame> Frames,
    IReadOnlyList<ActionSet> Sets);
