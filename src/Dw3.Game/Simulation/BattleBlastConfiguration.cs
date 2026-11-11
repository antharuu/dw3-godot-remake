namespace Dw3Production.Simulation;

/// <summary>Prepared Blast inputs belonging to one permanent partner, independent of its current form.</summary>
/// <param name="FormId">Resolved temporary form identity, not a one-based table index.</param>
/// <param name="PartnerIndex">Permanent partner definition ordinal selecting the return-delay family.</param>
/// <param name="Accessories">The two equipped accessory identities in slot order.</param>
public sealed record BattleBlastConfiguration(int FormId, int PartnerIndex, IReadOnlyList<int> Accessories);
