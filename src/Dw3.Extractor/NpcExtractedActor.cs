using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>One actor entry decoded from a stage overlay, with its resolved sprite archive reference.</summary>
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
