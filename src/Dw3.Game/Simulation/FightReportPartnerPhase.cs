namespace Dw3Production.Simulation;

/// <summary>
/// One partner panel's sequence: its fade in and out, and while it is raised, the walk over
/// its three Digimon slots, the level-up message and the wait for the level blink.
/// </summary>
internal enum FightReportPartnerPhase
{
    /// <summary>Shown and waiting. The report controller moves on when every panel is idle.</summary>
    Idle,

    /// <summary>Starts the 10-frame fade in.</summary>
    ShowStart,

    /// <summary>Fading in, then idle.</summary>
    ShowFade,

    /// <summary>Starts the fade out, twice as fast as the fade in.</summary>
    HideStart,

    /// <summary>Fading out, then hidden.</summary>
    HideFade,

    /// <summary>Fully transparent. The experience counter stops rolling.</summary>
    Hidden,

    /// <summary>Experience of the current Digimon slot. Not modelled yet: skipped and logged once.</summary>
    SlotExp,

    /// <summary>Level of the current Digimon slot, then the next slot.</summary>
    SlotLevel,

    /// <summary>Moves to the next of the three slots, or to the partner level after the third.</summary>
    NextSlot,

    /// <summary>Checks for a partner level-up and, if any, starts the blink and the level message.</summary>
    PartnerLevel,

    /// <summary>Waits until the level-up message is read.</summary>
    LevelMessage,

    /// <summary>Step reserved for a newly obtained Digimon. Passes straight to the blink wait.</summary>
    NewDigimon,

    /// <summary>Waits for the level blink to reach its last frame, then deselects the panel and goes idle.</summary>
    WaitBlinks,
}
