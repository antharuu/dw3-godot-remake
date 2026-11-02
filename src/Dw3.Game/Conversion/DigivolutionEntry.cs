namespace Dw3Production.Conversion;

/// <summary>One STFGTREP_evolutionLists entry: a Digimon a partner can learn, its
/// prerequisites, and a stat requirement (1-6 battle stat, 7 level, 8-14 resistance)
/// of at least <see cref="Value"/> (0 = no requirement).</summary>
public sealed record DigivolutionEntry(int DigimonIndex, int DigimonId, IReadOnlyList<EvolutionNeed> Needs, int Stat, int Value);
