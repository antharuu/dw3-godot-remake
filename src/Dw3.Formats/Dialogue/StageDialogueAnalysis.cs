namespace Dw3.Formats.Dialogue;

/// <summary>The dialogue inputs recovered for one stage overlay.</summary>
/// <param name="Mode">Field mode that selected the overlay.</param>
/// <param name="StageId">Numeric WSTAG identifier.</param>
/// <param name="TextFile">Language-specific talk file selected by setup code.</param>
/// <param name="ActorListAddress">RAM address of the actor pointer list, or null without actors.</param>
/// <param name="Actors">Dialogue-capable actors decoded from the validated list.</param>
public sealed record StageDialogueAnalysis(
    ushort Mode,
    int StageId,
    StageTextFile TextFile,
    uint? ActorListAddress,
    IReadOnlyList<StageActorDialogue> Actors);
