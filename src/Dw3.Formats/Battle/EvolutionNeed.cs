namespace Dw3.Formats.Battle;

/// <summary>A requested prerequisite for a permanent evolution.</summary>
public sealed record EvolutionNeed(short DigimonIndex, int DigimonId, short Level);
