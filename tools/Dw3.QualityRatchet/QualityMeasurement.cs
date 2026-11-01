namespace Dw3.QualityRatchet;

/// <summary>
/// What the last build and test run measured.
/// </summary>
/// <param name="Violations">The violation counts by key.</param>
/// <param name="Coverage">The merged coverage, or null when no Cobertura report was found.</param>
internal sealed record QualityMeasurement(IReadOnlyDictionary<ViolationKey, int> Violations, CoverageReport? Coverage);
