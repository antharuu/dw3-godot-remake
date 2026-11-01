namespace Dw3Production.Simulation;

/// <summary>Describes a visible Digimon slot in a party member's report panel.</summary>
public sealed record FightReportSlotView(int DigimonId, bool Visible, bool ExpApplied, int BlinkFrame);
