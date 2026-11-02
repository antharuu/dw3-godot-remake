using Dw3Production.Simulation;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Crosses one field exit by placing the player directly on its trigger cell, then pressing and
/// releasing Cross through the warp states Offer, commit, Rearm and Idle. Unlike
/// <see cref="WarpRouteDriver"/> it does not walk: it proves the warp contract of one edge, not
/// that the edge is reachable.
/// </summary>
internal static class WarpEdgeDriver
{
    /// <summary>Upper bound of ticks between Cross and the Rearm state.</summary>
    private const int MaxCommitTicks = 12;

    /// <summary>Crosses one exit.</summary>
    /// <param name="host">Running game.</param>
    /// <param name="sourceZone">Zone that must be applied when the edge starts.</param>
    /// <param name="slot">Trigger slot number, the low five bits of the trigger cell.</param>
    /// <param name="facing">Source facing (0-7) that the exit admits.</param>
    /// <param name="label">Edge name used in failure messages.</param>
    /// <exception cref="InvalidOperationException">The edge started elsewhere, or the warp skipped one of its states.</exception>
    public static void Run(IFieldProbeHost host, string sourceZone, int slot, int facing, string label)
    {
        var current = host.ActiveZone ?? throw new InvalidOperationException("probe has no current zone");
        ProbeRequirement.Require(current.Zone.Id == sourceZone, $"probe expected source={sourceZone}");
        host.State.Position = TriggerSlotLocator.FindCell(current, slot);
        host.State.SetFacing(facing);
        host.Input.State.ReleaseAction();
        Tick(host, current);
        ProbeRequirement.Require(host.Warp.State == WarpState.Offer, $"{label} did not offer");
        host.Input.State.PressAction();
        for (var tick = 0; tick < MaxCommitTicks && host.Warp.State != WarpState.Rearm; tick++)
        {
            Tick(host, host.CurrentZone);
        }

        ProbeRequirement.Require(host.Warp.State == WarpState.Rearm, $"{label} did not reach rearm");
        host.Input.State.ReleaseAction();
        Tick(host, host.CurrentZone);
        ProbeRequirement.Require(host.Warp.State == WarpState.Idle, $"{label} did not rearm after release");
    }

    /// <summary>Advances the warp state machine by one simulation tick with the host's own callbacks.</summary>
    /// <param name="host">Running game.</param>
    /// <param name="zone">Zone the player stands in for this tick.</param>
    private static void Tick(IFieldProbeHost host, FieldZone zone) =>
        host.Warp.Tick(host.State, host.Input.State, zone, host.CreateWarpCallbacks(host.Log.Warn));
}
