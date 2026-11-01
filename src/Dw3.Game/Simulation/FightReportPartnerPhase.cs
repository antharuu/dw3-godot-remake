namespace Dw3Production.Simulation;

/// <summary>
/// One partner panel's sequence: its fade in and out, and while it is raised, the walk over
/// its three Digimon slots, the level-up message and the wait for the level blink.
/// </summary>
internal enum FightReportPartnerPhase
{
    Idle,
    ShowStart,
    ShowFade,
    HideStart,
    HideFade,
    Hidden,
    SlotExp,
    SlotLevel,
    NextSlot,
    PartnerLevel,
    LevelMessage,
    NewDigimon,
    WaitBlinks,
}
