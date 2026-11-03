using Dw3Production.Logging;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// M3 climb acceptance on Central Park (S330): climbs a wall through its SLOT_CLIMB_UP slot and
/// descends through its SLOT_CLIMB_DOWN slot with Cross and the pad, using the decoded climb frames.
/// The climb height must reach the wall height, the landing must be on the ledge (up) or at the
/// foot (down), and a reversed climb must not leave the player stuck in the wall. The player is
/// placed on the trigger directly because the greedy walker cannot reach it.
/// </summary>
internal sealed class ClimbProbe : FieldProbe
{
    /// <summary>Slot type of SLOT_CLIMB_UP in the stage slot table.</summary>
    private const int ClimbUpType = 2;

    /// <summary>Slot type of SLOT_CLIMB_DOWN in the stage slot table.</summary>
    private const int ClimbDownType = 3;

    /// <summary>Horizontal distance between a climb slot and its landing, in world units.</summary>
    private const int LandingSideOffset = 16;

    /// <summary>Vertical distance between a climb slot and its landing, added to the wall height, in world units.</summary>
    private const int LandingDepthOffset = 24;

    /// <summary>
    /// Largest distance, in world units, at which a landing coordinate still counts as the expected one.
    /// Landings are whole world units; the tolerance only absorbs single-precision rounding.
    /// </summary>
    private const float LandingTolerance = 0.001f;

    /// <summary>Initializes a new instance of the <see cref="ClimbProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public ClimbProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "CLIMB_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory();
        var zone = Host.GetOrLoadZone("S330");
        Host.ApplyScene(zone);
        Host.State.ZoneId = "S330";
        var upSlot = zone.Zone.Climbs.FirstOrDefault(climb => climb.Type == ClimbUpType)
            ?? throw new InvalidOperationException("S330 has no SLOT_CLIMB_UP slot");
        var downSlot = zone.Zone.Climbs.FirstOrDefault(climb => climb.Type == ClimbDownType)
            ?? throw new InvalidOperationException("S330 has no SLOT_CLIMB_DOWN slot");
        var driver = new ClimbDriver(Host, captureRoot);

        var up = await driver.DriveAsync(zone, upSlot, climbUp: true);
        Require(up.MaxClimbHeight == upSlot.Height << 8, $"climb up reached {up.MaxClimbHeight} instead of wall height {upSlot.Height << 8}");
        Require(
            IsAt(Math.Abs(up.Final.X - upSlot.X), LandingSideOffset) && IsAt(up.Final.Y, upSlot.Y - LandingDepthOffset - upSlot.Height),
            $"climb up landed off-ledge actual=({up.Final.X},{up.Final.Y}) expected=({upSlot.X}±16,{upSlot.Y - LandingDepthOffset - upSlot.Height})");
        QuietLog.Info($"climb up PASS slot={upSlot.Slot} height={upSlot.Height} max_climb={up.MaxClimbHeight} final=({up.Final.X},{up.Final.Y}) dir={up.Direction}");

        var down = await driver.DriveAsync(zone, downSlot, climbUp: false);
        Require(down.MinClimbHeight == 0, $"climb down reached min {down.MinClimbHeight} instead of 0");
        Require(
            IsAt(Math.Abs(down.Final.X - downSlot.X), LandingSideOffset) && IsAt(down.Final.Y, downSlot.Y + LandingDepthOffset + downSlot.Height) && down.FinalFacing == 0,
            $"climb down landed at the foot actual=({down.Final.X},{down.Final.Y}) facing={down.FinalFacing} expected=({downSlot.X}±16,{downSlot.Y + LandingDepthOffset + downSlot.Height}) facing=0");
        QuietLog.Info($"climb down PASS slot={downSlot.Slot} height={downSlot.Height} min_climb={down.MinClimbHeight} final=({down.Final.X},{down.Final.Y}) dir={down.Direction}");

        // A reversed climb must land on a free cell: the depenetration pushes the foot off the wall.
        var upReverse = await driver.DriveReversalAsync(zone, upSlot, climbUp: true);
        Require(Host.CircleFreeAt(upReverse.Final), $"climb reverse-up landed stuck at ({upReverse.Final.X},{upReverse.Final.Y})");
        QuietLog.Info($"climb reverse-up slot={upSlot.Slot} final=({upReverse.Final.X},{upReverse.Final.Y}) facing={upReverse.FinalFacing}");
        var downReverse = await driver.DriveReversalAsync(zone, downSlot, climbUp: false);
        Require(Host.CircleFreeAt(downReverse.Final), $"climb reverse-down landed stuck at ({downReverse.Final.X},{downReverse.Final.Y})");
        QuietLog.Info($"climb reverse-down slot={downSlot.Slot} final=({downReverse.Final.X},{downReverse.Final.Y}) facing={downReverse.FinalFacing}");

        Require(!Host.Input.F3On, "climb probe enabled F3");
        QuietLog.Info($"climb probe PASS zone=S330 up_slot={upSlot.Slot} down_slot={downSlot.Slot} captures={captureRoot}/climb-*.png");
    }

    /// <summary>Tells whether a landing coordinate equals the expected whole world-unit value.</summary>
    /// <param name="actual">The measured coordinate, in world units.</param>
    /// <param name="expected">The expected coordinate, in world units.</param>
    /// <returns><see langword="true"/> when the two differ by less than <see cref="LandingTolerance"/>.</returns>
    private static bool IsAt(float actual, float expected) => Math.Abs(actual - expected) < LandingTolerance;
}
