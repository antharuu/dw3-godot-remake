namespace Dw3Production.Simulation;

/// <summary>Defines the source pair technique available from one active form with a named incoming form.</summary>
/// <param name="TechniqueId">Technique id resolved through the battle technique catalogue.</param>
/// <param name="PartnerNameId">Name-table id the incoming form must carry to match the source relation.</param>
public sealed record PairTechniqueDefinition(int TechniqueId, int PartnerNameId);
