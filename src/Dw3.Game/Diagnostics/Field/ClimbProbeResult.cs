using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>Outcome of one scripted climb.</summary>
/// <param name="MaxClimbHeight">Highest climb height reached, in 1/256 world units.</param>
/// <param name="MinClimbHeight">Lowest climb height reached, in 1/256 world units.</param>
/// <param name="Final">Player position once the climb released control.</param>
/// <param name="FinalFacing">Player facing once the climb released control.</param>
/// <param name="Direction">Trigger direction of the climb slot, 0-7.</param>
internal sealed record ClimbProbeResult(int MaxClimbHeight, int MinClimbHeight, NumericsVector2 Final, int FinalFacing, int Direction);
