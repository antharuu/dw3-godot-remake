namespace Dw3.QualityRatchet;

/// <summary>
/// The parsed command line of the coverage check, with every path made absolute.
/// </summary>
/// <param name="CoverageDirectory">The directory searched recursively for the coverage.cobertura.xml files written by the tests.</param>
/// <param name="MergedCoveragePath">Where the merged Cobertura report is written, for the CI artifact.</param>
/// <param name="BaselinePath">The baseline file holding the minimum and the checked libraries.</param>
internal sealed record RatchetOptions(string CoverageDirectory, string MergedCoveragePath, string BaselinePath);
