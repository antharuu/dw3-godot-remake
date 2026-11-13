namespace Dw3Production.Diagnostics.Battle;

/// <summary>One production tick observed while an entrance call holds the battle.</summary>
/// <param name="Tick">Production simulation tick since the call started.</param>
/// <param name="Phase">Call phase after the tick.</param>
/// <param name="Frame">Entrance frame after the tick.</param>
/// <param name="AfterTicks">Time units the post message has been open.</param>
/// <param name="Flash">Entrance white level.</param>
/// <param name="Rendered">Fighter rendered on the entering side.</param>
/// <param name="Hud">Fighter the HUD shows active on the entering side.</param>
/// <param name="Motion">Motion of the entering model.</param>
/// <param name="OpponentVisible">Whether the other side's model is drawn.</param>
/// <param name="Message">Debug label of the shown message, or empty when no box is shown.</param>
/// <param name="Owned">Whether the actor transition owns camera and visibility.</param>
/// <param name="Camera">Physical camera position.</param>
/// <param name="Flow">Battle flow phase.</param>
/// <param name="Stage">Fight stage id the scene shows.</param>
/// <param name="Music">Playing battle music and its fade volume.</param>
/// <param name="CuePlays">Entrance cues played so far.</param>
internal sealed record BattleEntranceTickSample(
    int Tick,
    string Phase,
    int Frame,
    int AfterTicks,
    int Flash,
    int Rendered,
    int Hud,
    int Motion,
    bool OpponentVisible,
    string Message,
    bool Owned,
    string Camera,
    string Flow,
    int Stage,
    string Music,
    int CuePlays);
