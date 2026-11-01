using Dw3.Formats.Field;

namespace Dw3.Extractor;

public sealed record NpcExtractedActor(
    int ActorIndex,
    int ActorId,
    int Image,
    int X,
    int Y,
    int Direction,
    int? Width,
    bool IsPlayerLead,
    bool SnapshotVisible,
    IReadOnlyList<NpcCondition> Conditions,
    NpcFileEntry? FileEntry,
    string? ArchiveDiscPath);
