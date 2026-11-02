using Dw3Production.Simulation;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// The M2 city loop S200 -> S210 -> S200 -> S235 -> S200: three distinct city zones crossed through
/// real trigger cells with native input, one commit per edge, a round trip and a re-entry. The
/// S210 slot 0 and S235 slot 1 return doors sit under their S200 arrival cells, so those returns
/// are immediate; S200 slot 5 (to S210) is beside spawn and S200 slot 4 (to S235) is the M1 route.
/// </summary>
internal static class CityLoopRoute
{
    /// <summary>Facing at <see cref="FinalArrival"/>.</summary>
    public const int FinalFacing = 7;

    /// <summary>Commits made by the loop, one per edge.</summary>
    public const int Commits = 4;

    /// <summary>S200 spawn decoded from the stage header, where the loop must start.</summary>
    public static readonly NumericsVector2 Spawn = new(1000, 236);

    /// <summary>S200 arrival through the S235 slot 1 door, where the loop ends.</summary>
    public static readonly NumericsVector2 FinalArrival = new(672, 378);

    /// <summary>The edges in order: source zone, exit slot, target zone, decoded arrival and facing.</summary>
    private static readonly (string Zone, int Slot, string Target, NumericsVector2 Arrival, int Facing)[] Legs =
    [
        ("S200", 5, "S210", new NumericsVector2(96, 400), 5),
        ("S210", 0, "S200", Spawn, 1),
        ("S200", 4, "S235", new NumericsVector2(278, 218), 3),
        ("S235", 1, "S200", FinalArrival, FinalFacing),
    ];

    /// <summary>Drives the loop, recording frames as the recorder samples them.</summary>
    /// <param name="host">Running game, standing at the S200 spawn.</param>
    /// <param name="frames">Recorder of the route frames.</param>
    /// <returns>A task that completes after the fourth commit.</returns>
    /// <exception cref="InvalidOperationException">An exit was not offered or did not commit its decoded arrival.</exception>
    public static async Task DriveAsync(IFieldProbeHost host, FrameSequenceRecorder frames)
    {
        var route = new WarpRouteDriver(host);
        foreach (var leg in Legs)
        {
            await route.WalkToExitAsync(leg.Zone, leg.Slot, frames);
            Require(host.Warp.State == WarpState.Offer, $"{leg.Zone} slot{leg.Slot} never offered on native input");
            await route.ActivateOfferedWarpAsync(leg.Target, leg.Arrival, leg.Facing, frames);
        }
    }

    /// <summary>Tells whether the player stands on the S200 spawn with the spawn facing.</summary>
    /// <param name="host">Running game.</param>
    /// <returns><see langword="true"/> when nothing repositioned the player since startup.</returns>
    public static bool StandsAtSpawn(IFieldProbeHost host) =>
        host.State.ZoneId == "S200" && host.State.Position == Spawn && host.State.Facing == 1;

    /// <summary>Tells whether the player is back in S200 at the arrival of the S235 slot 1 door.</summary>
    /// <param name="host">Running game.</param>
    /// <returns><see langword="true"/> when the loop ended where it should.</returns>
    public static bool EndedAtFinalArrival(IFieldProbeHost host) =>
        host.State.ZoneId == "S200" && host.State.Position == FinalArrival && host.State.Facing == FinalFacing;
}
