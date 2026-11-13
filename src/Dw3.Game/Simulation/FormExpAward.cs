namespace Dw3Production.Simulation;

/// <summary>The battle-wide inputs that size every used form's share of the encounter's form experience.</summary>
/// <param name="DigimonExp">Encounter form experience.</param>
/// <param name="PartnerLevel">Partner level before this battle's partner experience is added.</param>
/// <param name="GlobalUsed">Used equipped slots counted over every partner that fought.</param>
public sealed record FormExpAward(int DigimonExp, int PartnerLevel, int GlobalUsed);
