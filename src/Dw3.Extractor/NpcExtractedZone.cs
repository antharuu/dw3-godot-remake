namespace Dw3.Extractor;

/// <summary>All actors decoded from one stage's field overlay, keyed by stage ID.</summary>
public sealed record NpcExtractedZone(
    int StageId,
    string OverlayDiscPath,
    string OverlaySha256,
    IReadOnlyList<NpcExtractedActor> Actors);
