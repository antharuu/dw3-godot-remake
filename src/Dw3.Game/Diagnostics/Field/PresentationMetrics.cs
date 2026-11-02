using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>Outcome of one held-input presentation phase of the F3 performance probe.</summary>
/// <param name="Timing">Frame time statistics.</param>
/// <param name="Position">Player position after the phase; F3 must not change it.</param>
/// <param name="SimulationTicks">Simulation steps run during the measured frames.</param>
/// <param name="Rejections">Steps that locomotion refused.</param>
/// <param name="Work">Renderer work done during the measured frames.</param>
internal readonly record struct PresentationMetrics(FrameTimingStats Timing, NumericsVector2 Position, int SimulationTicks, int Rejections, PresentationWork Work);
