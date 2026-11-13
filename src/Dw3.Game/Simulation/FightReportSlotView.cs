namespace Dw3Production.Simulation;

/// <summary>Describes a Digimon slot row in a party member's report panel.</summary>
/// <param name="DigimonId">Form id shown in the row, -1 for a free row.</param>
/// <param name="Visible">Whether the row shows a form name and level.</param>
/// <param name="ExpApplied">Whether the form received experience in this report.</param>
/// <param name="BlinkFrame">Level-up blink colour row.</param>
/// <param name="Level">Form level shown.</param>
public sealed record FightReportSlotView(int DigimonId, bool Visible, bool ExpApplied, int BlinkFrame, int Level)
{
    /// <summary>Gets a value indicating whether the row's level-up blink is on.</summary>
    public bool Blinking { get; init; }
}
