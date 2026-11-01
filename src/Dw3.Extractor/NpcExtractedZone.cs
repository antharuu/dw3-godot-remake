namespace Dw3.Extractor;

public sealed record NpcExtractedZone(
    int StageId,
    string OverlayDiscPath,
    string OverlaySha256,
    IReadOnlyList<NpcExtractedActor> Actors);
