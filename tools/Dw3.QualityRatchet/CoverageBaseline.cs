namespace Dw3.QualityRatchet;

/// <summary>
/// The coverage section of the baseline file.
/// </summary>
/// <param name="TargetPercent">The line coverage every ratcheted library has to reach eventually.</param>
/// <param name="LinePercent">The lowest line coverage allowed per library, as a percentage.</param>
internal sealed record CoverageBaseline(double TargetPercent, IReadOnlyDictionary<string, double> LinePercent);
