namespace Dw3.QualityRatchet;

/// <summary>
/// The coverage section of the baseline file: one absolute threshold shared by every listed library.
/// The threshold is never derived from a measurement, so a refactor that splits lines cannot move it.
/// </summary>
/// <param name="MinimumPercent">The line coverage each listed library must reach, from 0 to 100. A library below it fails the check.</param>
/// <param name="Libraries">The assembly names, as Cobertura reports them, whose line coverage is checked.</param>
internal sealed record CoverageBaseline(double MinimumPercent, IReadOnlyList<string> Libraries);
