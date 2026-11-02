namespace Dw3Production.Diagnostics.Battle;

/// <summary>Stable luminance distribution measured inside one source-sized probe mask.</summary>
/// <param name="Count">Number of selected pixels.</param>
/// <param name="Mean">Average luminance.</param>
/// <param name="Q10">Tenth-percentile luminance.</param>
/// <param name="Q50">Median luminance.</param>
/// <param name="Q90">Ninetieth-percentile luminance.</param>
/// <param name="Histogram">Eight-bin histogram.</param>
internal sealed record BattleLuminanceStats(int Count, float Mean, float Q10, float Q50, float Q90, string Histogram);
