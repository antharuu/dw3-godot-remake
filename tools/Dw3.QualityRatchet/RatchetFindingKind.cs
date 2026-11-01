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
    /// The line coverage of a library went below its floor.
    /// </summary>
    CoverageDropped,

    /// <summary>
    /// A ratcheted library is absent from the coverage reports.
    /// </summary>
    CoverageMissing,

    /// <summary>
    /// A recorded count went down.
    /// </summary>
    CountDecreased,

    /// <summary>
    /// The line coverage of a library went above its floor.
    /// </summary>
    CoverageRaised,
}
