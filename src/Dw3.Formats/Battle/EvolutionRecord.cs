namespace Dw3.Formats.Battle;

/// <summary>One permanent evolution condition read from the fight-report overlay.</summary>
public sealed record EvolutionRecord(int DigimonIndex, int DigimonId, IReadOnlyList<EvolutionNeed> Needs, short Stat, short Value);
