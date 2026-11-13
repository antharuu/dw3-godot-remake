using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;

namespace Dw3Production.Startup;

/// <summary>
/// Everything a session reads from the data pack before the first scene is applied, plus the
/// values derived from it once (merged action animations, runtime growth tables, the test team).
/// Zones are not part of it: they are loaded on demand through <see cref="FieldLoader"/>.
/// </summary>
/// <remarks>
/// The three loaders stay open for the whole session because zones, frame images and battle
/// presentation resources are read lazily. Their owner disposes them when the session ends.
/// </remarks>
internal sealed class RuntimeAssets
{
    /// <summary>Gets the reader of the field tables: zones, player sprites and actors.</summary>
    public required FieldDataPackLoader FieldLoader { get; init; }

    /// <summary>Gets the reader of field animations: exit cues, climb and drop frames, map objects, connection icon.</summary>
    public required FieldAnimationDataPackLoader AnimationLoader { get; init; }

    /// <summary>Gets the reader of battle tables and battle presentation resources.</summary>
    public required BattleDataPackLoader BattleLoader { get; init; }

    /// <summary>Gets the player sprite animations.</summary>
    public required PlayerData Player { get; init; }

    /// <summary>Gets the exit-cue sprite animation shown over offered warps.</summary>
    public required ExitCue ExitCue { get; init; }

    /// <summary>Gets the climb animation sets.</summary>
    public required ClimbData Climb { get; init; }

    /// <summary>Gets the cliff-drop animation sets.</summary>
    public required ClimbData Drop { get; init; }

    /// <summary>Gets the climb and drop sets merged under one set id space, as the field renderer reads them.</summary>
    public required ClimbData Actions { get; init; }

    /// <summary>Gets the animated map objects (water, flags and similar tiles) of every zone.</summary>
    public required WaterData Water { get; init; }

    /// <summary>Gets the connection icon frames drawn above the player.</summary>
    public required PlayerIconData Icon { get; init; }

    /// <summary>Gets the field actors and their collision boxes for every zone.</summary>
    public required NpcData Npc { get; init; }

    /// <summary>Gets the per-zone battle tables and encounter definitions.</summary>
    public required EncounterData Encounters { get; init; }

    /// <summary>Gets the fighter table.</summary>
    public required FighterData Fighters { get; init; }

    /// <summary>Gets the technique table.</summary>
    public required TechniqueData Techniques { get; init; }

    /// <summary>Gets the digivolution table.</summary>
    public required DigivolutionData Digivolutions { get; init; }

    /// <summary>Gets the items usable in battle.</summary>
    public required BattleItemsData BattleItems { get; init; }

    /// <summary>Gets the battle menu and message text.</summary>
    public required BattleTextData BattleText { get; init; }

    /// <summary>Gets the battle rewards and the decoded growth tables.</summary>
    public required RewardsData Rewards { get; init; }

    /// <summary>Gets the runtime growth tables built from <see cref="Rewards"/>.</summary>
    public required GrowthTables GrowthTables { get; init; }

    /// <summary>Gets the in-memory player session shared by battles started during this process.</summary>
    public required PlayerSession PlayerSession { get; init; }

    /// <summary>Gets the field conversations and the dialogue font.</summary>
    public required DialogueData Dialogue { get; init; }

    /// <summary>Gets the talk-box frame sprites, panel and per-type layouts.</summary>
    public required DialogueFrameData DialogueFrames { get; init; }
}
