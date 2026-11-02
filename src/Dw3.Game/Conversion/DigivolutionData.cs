namespace Dw3Production.Conversion;

/// <summary>The m6-digivolution-v1 contract: the eight partners' evolution trees plus the blast tiers.</summary>
public sealed record DigivolutionData(IReadOnlyList<PartnerDigivolution> Partners, DigivolutionBlast Blast);
