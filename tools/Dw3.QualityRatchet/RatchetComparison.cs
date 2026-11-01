namespace Dw3.QualityRatchet;

/// <summary>
/// All differences between a measurement and the baseline.
/// </summary>
/// <param name="Findings">The differences, regressions and improvements alike.</param>
internal sealed record RatchetComparison(IReadOnlyList<RatchetFinding> Findings)
{
    /// <summary>
    /// Gets the differences that break the ratchet.
    /// </summary>
    public IEnumerable<RatchetFinding> Regressions => Findings.Where(finding => finding.IsRegression);

    /// <summary>
    /// Gets the differences that the update command can lock in.
    /// </summary>
    public IEnumerable<RatchetFinding> Improvements => Findings.Where(finding => !finding.IsRegression);

    /// <summary>
    /// Gets a value indicating whether any difference breaks the ratchet.
    /// </summary>
    public bool HasRegression => Regressions.Any();
}
