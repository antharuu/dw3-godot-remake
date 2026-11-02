namespace Dw3Production.Conversion;

/// <summary>One prerequisite of a digivolution entry: a Digimon the partner must
/// already have at least at a level (DigimonId 0 = none).</summary>
public sealed record EvolutionNeed(int DigimonIndex, int DigimonId, int Level);
