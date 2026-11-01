using System.Globalization;

namespace Dw3.QualityRatchet;

/// <summary>
/// One difference between a measurement and the baseline.
/// </summary>
/// <param name="Kind">The kind of difference.</param>
/// <param name="Subject">The violation key or the library name.</param>
/// <param name="Baseline">The recorded count or coverage floor.</param>
/// <param name="Actual">The measured count or coverage, NaN when the coverage is missing.</param>
internal sealed record RatchetFinding(RatchetFindingKind Kind, string Subject, double Baseline, double Actual)
{
    /// <summary>
    /// Gets a value indicating whether the difference breaks the ratchet.
    /// </summary>
    public bool IsRegression => Kind is RatchetFindingKind.CountIncreased
        or RatchetFindingKind.NewViolationPair
        or RatchetFindingKind.CoverageDropped
        or RatchetFindingKind.CoverageMissing;

    /// <summary>
    /// Describes the difference in one line, for example "S3776 Dw3.Game/Simulation: 12 -> 13".
    /// </summary>
    /// <returns>The description.</returns>
    public string Describe()
    {
        return Kind switch
        {
            RatchetFindingKind.NewViolationPair => $"new {Subject}: {Count(Actual)}",
            RatchetFindingKind.CountIncreased or RatchetFindingKind.CountDecreased =>
                $"{Subject}: {Count(Baseline)} -> {Count(Actual)}",
            RatchetFindingKind.CoverageMissing => $"coverage {Subject}: {Percent(Baseline)} -> no report",
            _ => $"coverage {Subject}: {Percent(Baseline)} -> {Percent(Actual)}",
        };
    }

    /// <summary>
    /// Formats a violation count.
    /// </summary>
    /// <param name="value">The count.</param>
    /// <returns>The invariant-culture integer.</returns>
    private static string Count(double value)
    {
        return ((int)value).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a coverage percentage with two decimals.
    /// </summary>
    /// <param name="value">The percentage.</param>
    /// <returns>The invariant-culture percentage followed by a percent sign.</returns>
    private static string Percent(double value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture) + "%";
    }
}
