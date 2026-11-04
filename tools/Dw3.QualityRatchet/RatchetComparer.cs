namespace Dw3.QualityRatchet;

/// <summary>
/// Compares merged line coverage with the absolute minimum of the baseline.
/// </summary>
internal static class RatchetComparer
{
    /// <summary>
    /// Checks the line coverage of every listed library against the minimum.
    /// Coverage above the minimum is not a finding: nothing is recorded, so a refactor that changes
    /// the number of measured lines cannot turn into a false regression.
    /// </summary>
    /// <param name="recorded">The coverage section of the baseline.</param>
    /// <param name="coverage">The merged coverage of the test run.</param>
    /// <returns>One finding per library that is missing from the reports or below the minimum, in ordinal order. Empty when the check passes.</returns>
    public static IReadOnlyList<RatchetFinding> Compare(CoverageBaseline recorded, CoverageReport coverage)
    {
        double minimum = recorded.MinimumPercent;
        List<RatchetFinding> findings = [];
        foreach (string library in recorded.Libraries.Order(StringComparer.Ordinal))
        {
            double? actual = coverage.LinePercent(library);
            if (actual is null)
            {
                findings.Add(new RatchetFinding(RatchetFindingKind.CoverageMissing, library, minimum, double.NaN));
            }
            else if (actual < minimum)
            {
                findings.Add(new RatchetFinding(RatchetFindingKind.CoverageBelowMinimum, library, minimum, actual.Value));
            }
        }

        return findings;
    }
}
