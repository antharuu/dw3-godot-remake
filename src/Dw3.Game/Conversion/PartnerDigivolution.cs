namespace Dw3Production.Conversion;

/// <summary>One partner's evolution tree: its base Digimon and its 44 learnable forms.</summary>
public sealed record PartnerDigivolution(int Partner, int BaseId, IReadOnlyList<DigivolutionEntry> Evolutions);
