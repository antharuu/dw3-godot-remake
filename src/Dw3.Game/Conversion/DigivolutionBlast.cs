namespace Dw3Production.Conversion;

/// <summary>The in-battle Digivolve command's level tiers (WFIGHTMN_blast): the partner
/// level below which each DigimonData.BlastForms tier applies.</summary>
public sealed record DigivolutionBlast(IReadOnlyList<int> TierLevels);
