namespace Dw3.QualityRatchet;

/// <summary>
/// Builds the next baseline from a measurement: violation counts can only go down. The coverage section
/// is an absolute minimum and is carried over unchanged.
/// </summary>
internal static class BaselineTightener
{
    /// <summary>
    /// Locks the improvements of a measurement into a new baseline.
    /// Keys whose count reached zero leave the baseline, so their rule can no longer come back there.
    /// </summary>
    /// <param name="baseline">The current baseline.</param>
    /// <param name="measurement">The measurement.</param>
    /// <returns>The tightened baseline.</returns>
    /// <exception cref="RatchetRefusedException">When the measurement has any regression.</exception>
    public static QualityBaseline Tighten(QualityBaseline baseline, QualityMeasurement measurement)
    {
        RatchetComparison comparison = RatchetComparer.Compare(baseline, measurement);
        if (comparison.HasRegression)
        {
            throw new RatchetRefusedException([.. comparison.Regressions.Select(finding => finding.Describe())]);
        }

        List<BaselineViolation> violations =
        [
            .. measurement.Violations
                .Where(entry => entry.Value > 0)
                .Select(entry => new BaselineViolation(entry.Key.Rule, entry.Key.Project, entry.Key.Folder, entry.Value)),
        ];
        return new QualityBaseline(baseline.Coverage, violations);
    }

    /// <summary>
    /// Lists the rules that the baseline lists but the tightened baseline no longer does.
    /// </summary>
    /// <param name="before">The baseline before the update.</param>
    /// <param name="after">The baseline after the update.</param>
    /// <returns>The rule identifiers in ordinal order.</returns>
    public static IReadOnlyList<string> ExhaustedRules(QualityBaseline before, QualityBaseline after)
    {
        return
        [
            .. before.Violations.Select(entry => entry.Rule)
                .Except(after.Violations.Select(entry => entry.Rule), StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }
}
