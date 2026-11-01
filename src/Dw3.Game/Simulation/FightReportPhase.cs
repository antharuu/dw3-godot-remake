namespace Dw3Production.Simulation;

/// <summary>
/// The battle report's sequence, in play order: the panels fade in, each partner that earned
/// experience is raised in turn, the money and item messages follow, then everything fades out.
/// </summary>
internal enum FightReportPhase
{
    Initial,
    ShowPanels,
    WaitPanels,
    FooterIn,
    SelectPartner,
    HeaderIn,
    ExpMessage,
    RaisePartner,
    HeaderOut,
    Money,
    ItemStart,
    ItemMessage,
    StartExit,
    BeginExit,
    ExitPanels,
    ExitFade,
    Done,
}
