namespace Dw3.QualityRatchet;

/// <summary>
/// Compares a measurement with the baseline.
/// </summary>
internal static class RatchetComparer
{
    /// <summary>
    /// Lists every count and coverage difference. Coverage is compared only when the measurement has it.
    /// </summary>
    /// <param name="baseline">The baseline.</param>
    /// <param name="measurement">The measurement.</param>
    /// <returns>The differences, violation counts first.</returns>
    public static RatchetComparison Compare(QualityBaseline baseline, QualityMeasurement measurement)
    {
        List<RatchetFinding> findings = [.. CompareViolations(baseline.ViolationCounts(), measurement.Violations)];
        if (measurement.Coverage is not null)
        {
            findings.AddRange(CompareCoverage(baseline.Coverage, measurement.Coverage));
        }

        return new RatchetComparison(findings);
    }

    /// <summary>
    /// Compares the violation counts key by key.
    /// </summary>
    /// <param name="recorded">The baseline counts.</param>
    /// <param name="measured">The measured counts.</param>
    /// <returns>The count differences in key order.</returns>
    private static IEnumerable<RatchetFinding> CompareViolations(
        IReadOnlyDictionary<ViolationKey, int> recorded,
        IReadOnlyDictionary<ViolationKey, int> measured)
    {
        return recorded.Keys
            .Union(measured.Keys)
            .Order(ViolationKeyComparer.Instance)
            .Select(key => CompareCount(key, recorded.TryGetValue(key, out int count) ? count : null, measured.GetValueOrDefault(key)))
            .OfType<RatchetFinding>();
    }

    /// <summary>
    /// Compares the count of one key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="recorded">The baseline count, null when the baseline does not list the key.</param>
    /// <param name="actual">The measured count.</param>
    /// <returns>The difference, or null when the count is unchanged.</returns>
    private static RatchetFinding? CompareCount(ViolationKey key, int? recorded, int actual)
    {
        int ceiling = recorded ?? 0;
        if (actual == ceiling)
        {
            return null;
        }

        if (actual < ceiling)
        {
            return new RatchetFinding(RatchetFindingKind.CountDecreased, key.ToString(), ceiling, actual);
        }

        RatchetFindingKind kind = recorded is null ? RatchetFindingKind.NewViolationPair : RatchetFindingKind.CountIncreased;
        return new RatchetFinding(kind, key.ToString(), ceiling, actual);
    }

    /// <summary>
    /// Compares the line coverage of every library the baseline ratchets.
    /// </summary>
    /// <param name="recorded">The coverage section of the baseline.</param>
    /// <param name="coverage">The merged coverage.</param>
    /// <returns>The coverage differences in library order.</returns>
    private static IEnumerable<RatchetFinding> CompareCoverage(CoverageBaseline recorded, CoverageReport coverage)
    {
        foreach ((string library, double floor) in recorded.LinePercent.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            double? actual = coverage.LinePercent(library);
            RatchetFinding? finding = actual switch
            {
                null => new RatchetFinding(RatchetFindingKind.CoverageMissing, library, floor, double.NaN),
                _ when actual < floor => new RatchetFinding(RatchetFindingKind.CoverageDropped, library, floor, actual.Value),
                _ when actual > floor => new RatchetFinding(RatchetFindingKind.CoverageRaised, library, floor, actual.Value),
                _ => null,
            };
            if (finding is not null)
            {
                yield return finding;
            }
        }
    }
}
