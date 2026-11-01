namespace Dw3.Extractor;

/// <summary>Complete decoded NPC character archive ready to write to the data pack.</summary>
public sealed record NpcExtractedCharacter(
    int ActorId,
    int StageId,
    int ActorIndex,
    string ManifestSchema,
    string ArchiveDiscPath,
    string ArchiveSha256,
    int ArchiveBytes,
    int ArchiveEntry,
    IReadOnlyList<NpcExtractedFrame> Frames,
    IReadOnlyList<NpcAnimSet> AnimSets,
    IReadOnlyList<NpcAnimSequence> Sequences);
