namespace Dw3.QualityRatchet;

/// <summary>
/// The content of tools/quality-baseline.json. It holds only the coverage section: analyzer rules
/// have no baseline because any diagnostic fails the build.
/// </summary>
/// <param name="Coverage">The coverage minimum and the libraries it applies to.</param>
internal sealed record QualityBaseline(CoverageBaseline Coverage);
