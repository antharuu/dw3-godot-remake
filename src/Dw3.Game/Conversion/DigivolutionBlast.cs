namespace Dw3Production.Conversion;

/// <summary>The in-battle blast digivolution's level tiers: the partner
/// level below which each DigimonData.BlastForms tier applies.</summary>
public sealed record DigivolutionBlast(IReadOnlyList<int> TierLevels);
