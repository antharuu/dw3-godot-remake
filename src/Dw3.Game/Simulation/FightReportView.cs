namespace Dw3Production.Simulation;

/// <summary>Provides a complete frame snapshot for the battle reward report.</summary>
public sealed record FightReportView(
    int HeaderFade,
    int FooterFade,
    int BgScroll,
    IReadOnlyList<FightReportPartnerView> Partners,
    FightReportMessage? Message,
    int ExitFade,
    bool Done)
{
    /// <summary>Gets the selected partner's experience shown in the header window with its label, or null while the header carries none.</summary>
    public int? HeaderExp { get; init; }
}
