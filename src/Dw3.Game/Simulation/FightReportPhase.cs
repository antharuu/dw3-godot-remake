namespace Dw3Production.Simulation;

/// <summary>
/// The battle report's sequence, in play order: the panels fade in, each partner that earned
/// experience is raised in turn, the money and item messages follow, then everything fades out.
/// </summary>
internal enum FightReportPhase
{
    /// <summary>First frame, before anything is shown.</summary>
    Initial,

    /// <summary>Starts every partner panel's fade in.</summary>
    ShowPanels,

    /// <summary>Waits for every panel to be idle, then starts the footer fade in.</summary>
    WaitPanels,

    /// <summary>Footer fading in.</summary>
    FooterIn,

    /// <summary>Picks the next partner with gained experience, or moves to the money message when none is left.</summary>
    SelectPartner,

    /// <summary>Header fading in for the selected partner, then its experience message.</summary>
    HeaderIn,

    /// <summary>Waits until the experience message is read, then starts the panel's experience sequence.</summary>
    ExpMessage,

    /// <summary>Waits for the raised panel to finish its sequence.</summary>
    RaisePartner,

    /// <summary>Header fading out, then the next partner.</summary>
    HeaderOut,

    /// <summary>Shows the money message and waits until it is read.</summary>
    Money,

    /// <summary>Shows the dropped item message.</summary>
    ItemStart,

    /// <summary>Waits until the item message is read.</summary>
    ItemMessage,

    /// <summary>Taken when the battle dropped no item, one frame before the exit starts.</summary>
    StartExit,

    /// <summary>Starts the 30-frame exit fade and fades the footer and panels out.</summary>
    BeginExit,

    /// <summary>Waits until the footer and every panel are hidden.</summary>
    ExitPanels,

    /// <summary>Waits for the exit fade to complete.</summary>
    ExitFade,

    /// <summary>The report is over. Further steps do nothing.</summary>
    Done,
}
