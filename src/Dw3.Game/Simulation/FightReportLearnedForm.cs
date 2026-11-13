namespace Dw3Production.Simulation;

/// <summary>Describes one newly acquired form and the decoded name entry the report message displays.</summary>
/// <param name="DigimonId">Persistent acquired-form identifier.</param>
/// <param name="NameId">Fight-report text substitution for the form's decoded name.</param>
public sealed record FightReportLearnedForm(int DigimonId, int NameId);
