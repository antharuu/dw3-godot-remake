namespace Dw3Production.Diagnostics.Field;

/// <summary>Frame time statistics of one measured presentation phase.</summary>
/// <param name="Frames">Measured frames.</param>
/// <param name="MedianMs">Median time from simulation step to drawn frame, in milliseconds.</param>
/// <param name="P95Ms">95th percentile of the same time, in milliseconds.</param>
/// <param name="Fps">Measured frames divided by the wall-clock duration of the phase.</param>
internal readonly record struct FrameTimingStats(int Frames, double MedianMs, double P95Ms, double Fps);
