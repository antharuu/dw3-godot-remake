namespace Dw3.Formats.Field;

/// <summary>
/// Provides NPC actor table data derived from the field stage executable tables.
///
/// This interface isolates the disc-executable-reading layer from the format decoders.
/// Implementations read FIELDSTG_fileEntries, FIELDSTG_actorWidths, actor-list
/// addresses, and the FILE_SECTORS table directly from supported disc members.
/// </summary>
public interface INpcActorTableSource
{
    /// <summary>
    /// Returns the file-system entry for the sprite archive of the given actor_id,
    /// or null if the actor_id has no archive (player lead, non-visual NPC, etc.).
    ///
    /// Disc: FIELDSTG_fileEntries[actorId] encodes (fileNumber &lt;&lt; 16) | archiveEntry.
    /// </summary>
    /// <param name="actorId">Zero-based actor id from the stage's actor list.</param>
    /// <returns>File number and archive entry, or null when the actor has no sprite archive.</returns>
    (int FileNumber, int ArchiveEntry)? GetActorSpriteEntry(int actorId);

    /// <summary>
    /// Returns the sprite collision width in pixels for the given actor_id, or null if unknown.
    ///
    /// Disc: FIELDSTG_actorWidths[actorId].
    /// </summary>
    /// <param name="actorId">Zero-based actor id from the stage's actor list.</param>
    /// <returns>Collision width in pixels, or null when the id is not in the width table.</returns>
    int? GetActorWidth(int actorId);

    /// <summary>Returns true when the actor_id belongs to the player lead (not an NPC).</summary>
    /// <param name="actorId">Zero-based actor id to test.</param>
    /// <returns><see langword="true"/> when the actor is the player lead; <see langword="false"/> for NPCs.</returns>
    bool IsPlayerLead(int actorId);

    /// <summary>
    /// Returns the ordered actor entries recovered from the given stage overlay.
    /// Each entry provides the RAM address and the sequential index of one
    /// FieldActorEntry within the stage overlay.
    ///
    /// Disc: null-terminated actor pointer list stored by the overlay setup routine.
    /// </summary>
    /// <param name="stageId">Stage identifier whose overlay to scan for actor entries.</param>
    /// <returns>Ordered list of actor references, in the order they appear in the overlay.</returns>
    IReadOnlyList<NpcActorReference> GetStageActors(int stageId);

    /// <summary>Evaluates whether a numeric condition list holds at the manifest snapshot.</summary>
    /// <param name="conditions">List of conditions read from the NPC's visibility record.</param>
    /// <returns><see langword="true"/> when all conditions in <paramref name="conditions"/> are satisfied.</returns>
    bool IsVisible(IReadOnlyList<NpcCondition> conditions);

    /// <summary>
    /// Returns the ISO path of the disc file for the given file number, or null if unknown.
    ///
    /// Disc: FILE_SECTORS[fileNumber] maps file indices to disc sector addresses.
    /// </summary>
    /// <param name="fileNumber">Disc file-sector table index.</param>
    /// <returns>ISO path string (e.g. "AAA/WSTAG200.BIN"), or null when the index is out of range.</returns>
    string? GetDiscFilePath(int fileNumber);

    /// <summary>
    /// Returns the trigger archive entry index used by the given stage for TMPK trigger maps,
    /// or null if the stage has no trigger map.
    ///
    /// Disc: per-stage constant derived from the TMPK archive header.
    /// </summary>
    /// <param name="stageId">Stage identifier to query.</param>
    /// <returns>Archive entry index for the trigger map, or null when the stage has no trigger map.</returns>
    int? GetTriggerArchiveEntry(int stageId);

    /// <summary>
    /// Returns the ordered list of Required stage identifiers to extract.
    ///
    /// Disc: derived from the stage mode table (FIELDSTG_stages, EU version).
    /// </summary>
    /// <returns>Stage ids in extraction order; the list is never empty for a valid disc.</returns>
    IReadOnlyList<int> GetRequiredStageIds();
}

/// <summary>One actor reference from a stage overlay: its RAM address and sequential index.</summary>
/// <param name="Address">RAM address of the FieldActorEntry in the stage overlay.</param>
/// <param name="Index">Sequential index within the stage (actor0, actor1, …).</param>
public sealed record NpcActorReference(uint Address, int Index);
