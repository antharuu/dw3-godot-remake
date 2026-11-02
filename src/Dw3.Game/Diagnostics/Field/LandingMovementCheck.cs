using Dw3Production.Logging;
using Dw3Production.Simulation;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Tries one locomotion step in each of the eight directions from a climb or drop landing, logging
/// each result and restoring the position, so a landing stuck in a wall shows in the probe output.
/// </summary>
internal static class LandingMovementCheck
{
    /// <summary>Tries the eight directions and restores the landing position and facing.</summary>
    /// <param name="host">Running game, standing on the landing.</param>
    /// <param name="slot">Climb or drop slot, for the log.</param>
    /// <returns>The number of directions in which the player could move.</returns>
    public static int Run(IFieldProbeHost host, int slot)
    {
        var state = host.State;
        var landingFacing = state.Facing;
        var movedDirections = 0;
        foreach (var probeDir in Enum.GetValues<SemanticDirection>().Where(direction => direction != SemanticDirection.None))
        {
            var savePos = state.Position;
            host.Input.State.SetDirections(
                left: probeDir is SemanticDirection.Left or SemanticDirection.UpLeft or SemanticDirection.DownLeft,
                right: probeDir is SemanticDirection.Right or SemanticDirection.UpRight or SemanticDirection.DownRight,
                up: probeDir is SemanticDirection.Up or SemanticDirection.UpLeft or SemanticDirection.UpRight,
                down: probeDir is SemanticDirection.Down or SemanticDirection.DownLeft or SemanticDirection.DownRight);
            var move = Locomotion.Tick(state, host.Input.State.Direction, host.Floor!, host.Occupants);
            movedDirections += move.Moved ? 1 : 0;
            QuietLog.Info($"climb-after-move slot={slot} dir={probeDir} moved={move.Moved} rejection={move.Rejection ?? "none"} step=({move.Step.X},{move.Step.Y}) pos=({state.Position.X},{state.Position.Y})");
            state.Position = savePos;
            host.Input.State.SetDirections(left: false, right: false, up: false, down: false);
        }

        state.SetFacing(landingFacing);
        return movedDirections;
    }
}
