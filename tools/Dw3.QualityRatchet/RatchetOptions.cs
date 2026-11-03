namespace Dw3.QualityRatchet;

/// <summary>
/// The parsed command line of the ratchet, with every path made absolute.
/// </summary>
/// <param name="Mode">The command to run.</param>
/// <param name="SolutionPath">The solution listing the projects to measure.</param>
/// <param name="SarifDirectory">The directory of the SARIF logs written by the build.</param>
/// <param name="CoverageDirectory">The directory searched for Cobertura reports written by the tests.</param>
/// <param name="MergedCoveragePath">Where the merged Cobertura report is written.</param>
/// <param name="BaselinePath">The baseline file.</param>
/// <param name="RequireCoverage">Whether a missing Cobertura report is an error rather than a skipped check.</param>
/// <param name="Libraries">The libraries whose coverage the init command ratchets.</param>
/// <param name="CoverageTarget">The coverage minimum the init command records.</param>
internal sealed record RatchetOptions(
    RatchetMode Mode,
    string SolutionPath,
    string SarifDirectory,
    string CoverageDirectory,
    string MergedCoveragePath,
    string BaselinePath,
    bool RequireCoverage,
    IReadOnlyList<string> Libraries,
    double CoverageTarget);
