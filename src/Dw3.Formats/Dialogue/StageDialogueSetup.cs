using Dw3.Formats.Field;

namespace Dw3.Formats.Dialogue;

/// <summary>
/// Dialogue state materialized by a stage setup routine.
/// </summary>
/// <param name="TextSelector">Immediate added to the language global to select a talk file.</param>
/// <param name="ActorList">Validated actor pointer list, or null when setup stores no list.</param>
public sealed record StageDialogueSetup(int TextSelector, NpcStageActorList? ActorList);
