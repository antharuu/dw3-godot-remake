using Dw3Production.Simulation;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>Greedy steering used by the scripted walks: try the pad directions that bring the player closest first.</summary>
internal static class ProbeSteering
{
    /// <summary>Orders the eight pad directions by the distance left to the target after a one-unit step.</summary>
    /// <param name="from">Current position in world units.</param>
    /// <param name="target">Position to reach.</param>
    /// <returns>The eight directions, best first; ties keep the enum order.</returns>
    public static IEnumerable<SemanticDirection> DirectionsToward(NumericsVector2 from, NumericsVector2 target) =>
        Enum.GetValues<SemanticDirection>()
            .Where(direction => direction != SemanticDirection.None)
            .OrderBy(direction => NumericsVector2.DistanceSquared(from + UnitStep(direction), target));

    /// <summary>Holds the best direction that moves the player, one simulation step.</summary>
    /// <param name="host">Running game.</param>
    /// <param name="target">Position to reach.</param>
    /// <returns><see langword="true"/> when one direction moved the player.</returns>
    public static bool StepToward(IFieldProbeHost host, NumericsVector2 target) =>
        DirectionsToward(host.State.Position, target).Any(host.TryTestMove);

    /// <summary>Returns the one-unit step of a pad direction, with Y growing downwards.</summary>
    /// <param name="direction">Pad direction.</param>
    /// <returns>The step vector, zero for <see cref="SemanticDirection.None"/>.</returns>
    private static NumericsVector2 UnitStep(SemanticDirection direction) => direction switch
    {
        SemanticDirection.Up => new NumericsVector2(0, -1),
        SemanticDirection.UpRight => new NumericsVector2(1, -1),
        SemanticDirection.Right => new NumericsVector2(1, 0),
        SemanticDirection.DownRight => new NumericsVector2(1, 1),
        SemanticDirection.Down => new NumericsVector2(0, 1),
        SemanticDirection.DownLeft => new NumericsVector2(-1, 1),
        SemanticDirection.Left => new NumericsVector2(-1, 0),
        SemanticDirection.UpLeft => new NumericsVector2(-1, -1),
        _ => NumericsVector2.Zero,
    };
}
