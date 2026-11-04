using System.Globalization;

namespace Dw3.QualityRatchet;

/// <summary>
/// One library that fails the coverage check.
/// </summary>
/// <param name="Kind">Whether the library is below the minimum or absent from every report.</param>
/// <param name="Library">The assembly name, as Cobertura reports it.</param>
/// <param name="Minimum">The line coverage minimum of the baseline, in percent.</param>
/// <param name="Actual">The measured line coverage in percent, NaN when the library is absent from the reports.</param>
internal sealed record RatchetFinding(RatchetFindingKind Kind, string Library, double Minimum, double Actual)
{
    /// <summary>
    /// Describes the failure in one line, for example "coverage Dw3.Data: 25.00% is below the 80.00% minimum".
    /// </summary>
    /// <returns>The description, percentages formatted with two decimals in the invariant culture.</returns>
    public string Describe()
    {
        return Kind == RatchetFindingKind.CoverageMissing
            ? $"coverage {Library}: no report (minimum {Percent(Minimum)})"
            : $"coverage {Library}: {Percent(Actual)} is below the {Percent(Minimum)} minimum";
    }

    /// <summary>
    /// Formats a coverage percentage with two decimals.
    /// </summary>
    /// <param name="value">The percentage, from 0 to 100.</param>
    /// <returns>The invariant-culture percentage followed by a percent sign.</returns>
    private static string Percent(double value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture) + "%";
    }
}
