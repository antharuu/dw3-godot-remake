namespace Dw3.QualityRatchet;

/// <summary>
/// The kinds of difference between a measurement and the baseline.
/// </summary>
internal enum RatchetFindingKind
{
    /// <summary>
    /// A recorded count went up.
    /// </summary>
    CountIncreased,

    /// <summary>
    /// A rule, project and folder that the baseline does not list now has violations.
    /// </summary>
    NewViolationPair,

    /// <summary>
    /// The line coverage of a library is below the minimum of the baseline.
    /// </summary>
    CoverageBelowMinimum,

    /// <summary>
    /// A listed library is absent from the coverage reports.
    /// </summary>
    CoverageMissing,

    /// <summary>
    /// A recorded count went down.
    /// </summary>
    CountDecreased,
}
