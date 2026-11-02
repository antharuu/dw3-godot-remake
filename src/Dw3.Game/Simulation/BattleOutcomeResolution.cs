using Dw3Production.Presentation;

namespace Dw3Production.Simulation;

/// <summary>Contains the presentation and state-machine inputs produced by victory resolution.</summary>
/// <param name="Report">Battle HUD victory summary.</param>
/// <param name="FightReport">Detailed report state-machine input.</param>
/// <param name="Log">Structured reward diagnostic line.</param>
public sealed record BattleOutcomeResolution(
    BattleReportView Report,
    FightReportInput FightReport,
    string Log);
