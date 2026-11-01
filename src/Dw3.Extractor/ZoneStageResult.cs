using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>
/// Holds all data decoded from the disc for a single field stage during the zone
/// extraction pass. Passed between <c>DecodeStageData</c> and its writing helpers
/// to avoid repeating parameter lists.
/// </summary>
/// <param name="Terrain">Decoded terrain layers and tile records from the stage packet.</param>
/// <param name="Floor">Floor TMPK map that drives tile-placement lookups at runtime.</param>
/// <param name="TriggerMap">Trigger TMPK map, or <c>null</c> when the stage has none.</param>
/// <param name="AreasMap">Battle-zone TMPK map, or <c>null</c> when the stage has none.</param>
/// <param name="AreasEntryIndex">Index of the selected areas TMPK entry, or <c>null</c>.</param>
/// <param name="RequiredOffsets">Byte offsets of all TMPK entries the runtime must load.</param>
/// <param name="Selection">Floor and trigger entry indices recovered from the overlay.</param>
/// <param name="ResolvedTriggerEntry">Resolved trigger entry after applying disc overrides.</param>
/// <param name="Slots">Stage-slot list recovered from the overlay, empty for no-slot stages.</param>
/// <param name="StageColor">Stream depth-11 colour modulate bytes, or <c>null</c> when absent.</param>
internal sealed record ZoneStageResult(
    ZoneTerrain Terrain,
    TmpkMap Floor,
    TmpkMap? TriggerMap,
    TmpkMap? AreasMap,
    int? AreasEntryIndex,
    int[] RequiredOffsets,
    TmpkMapSelection Selection,
    int? ResolvedTriggerEntry,
    IReadOnlyList<StageSlot> Slots,
    byte[]? StageColor);
