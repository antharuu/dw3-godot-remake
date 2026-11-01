namespace Dw3.Formats.Field;

/// <summary>Decoded NPC actor entry from a field stage overlay (.PRO file).</summary>
/// <param name="ConditionsPtr">RAM pointer to the actor's condition list (0 = unconditional).</param>
/// <param name="TalksPtr">RAM pointer to the actor's dialogue table (0 = no dialogue).</param>
/// <param name="ActorId">Character identifier, indexes FIELDSTG_fileEntries and FIELDSTG_actorWidths.</param>
/// <param name="Image">VRAM image slot index (FIELDSTG_drawActor placement, not sprite selection).</param>
/// <param name="X">Pixel X position on the stage map.</param>
/// <param name="Y">Pixel Y position on the stage map.</param>
/// <param name="Direction">Facing direction in 0..7.</param>
public sealed record NpcActorEntry(
    uint ConditionsPtr,
    uint TalksPtr,
    short ActorId,
    short Image,
    short X,
    short Y,
    short Direction);
