using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>
/// All data needed to write a complete zone manifest, derived exclusively from
/// the user's disc during the extraction pass. No game-specific content is
/// hard-coded here; every value comes from disc bytes decoded by the format readers.
/// </summary>
public sealed record ZoneManifestData(
    string Zone,
    string PackPath,
    int PackBytes,
    string PackSha256,
    int TileCount,
    int TileColumns,
    int TileRows,
    int TilePacketBytes,
    string TmpkPath,
    int TmpkBytes,
    string TmpkSha256,
    int FloorEntry,
    int? TriggerEntry,
    int? AreasEntry,
    int[] RequiredOffsets,
    TmpkMap FloorMap,
    TmpkMap? TriggerMap,
    TmpkMap? AreasMap,
    IReadOnlyList<ZoneTileRecord> TileRecords,
    byte[]? StreamDepth11Modulate,
    IReadOnlyList<StageSlot> Slots,
    IReadOnlyDictionary<ushort, int>? StageModes,
    string Status
);
