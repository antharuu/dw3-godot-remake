namespace Dw3.QualityRatchet;

/// <summary>
/// The ways a listed library can fail the coverage check.
/// </summary>
internal enum RatchetFindingKind
{
    /// <summary>
    /// The line coverage of the library is below the minimum of the baseline.
    /// </summary>
    CoverageBelowMinimum,

    /// <summary>
    /// The library is absent from every Cobertura report, usually because no test project references it.
    /// </summary>
    CoverageMissing,
}
