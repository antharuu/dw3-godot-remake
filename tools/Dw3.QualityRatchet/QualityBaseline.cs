namespace Dw3.QualityRatchet;

/// <summary>
/// The content of tools/quality-baseline.json: the ceiling of every violation count and the floor of
/// the line coverage of every ratcheted library.
/// </summary>
/// <param name="Coverage">The coverage floors and target.</param>
/// <param name="Violations">The violation ceilings, one entry per rule, project and folder.</param>
internal sealed record QualityBaseline(CoverageBaseline Coverage, IReadOnlyList<BaselineViolation> Violations)
{
    /// <summary>
    /// Indexes the violation ceilings by key.
    /// </summary>
    /// <returns>The counts by key.</returns>
    public IReadOnlyDictionary<ViolationKey, int> ViolationCounts()
    {
        return Violations.ToDictionary(entry => entry.Key, entry => entry.Count);
    }
}
