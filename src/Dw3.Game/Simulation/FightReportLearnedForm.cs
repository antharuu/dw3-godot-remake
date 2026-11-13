namespace Dw3Production.Simulation;

/// <summary>Describes one form a partner acquired after a battle.</summary>
/// <param name="DigimonId">Persistent form identifier.</param>
/// <param name="NameId">Decoded Digimon name entry shown by the report message.</param>
public sealed record FightReportLearnedForm(int DigimonId, int NameId)
{
    /// <summary>Gets the form's level once acquired, shown when the form takes a free slot row. A new entry starts at 1.</summary>
    public int Level { get; init; } = 1;
}
