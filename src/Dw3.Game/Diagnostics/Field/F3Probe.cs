using Dw3Production.Logging;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Crosses the S200 slot 4 exit to S235 with the F3 overlay off and again with it on, and checks
/// that the overlay changes neither the arrival, the facing, the story progress nor the state
/// around the toggles. Captures the overlay off and on in both zones and requires the player
/// collision circle to be drawn.
/// </summary>
internal sealed class F3Probe : FieldProbe
{
    /// <summary>Initializes a new instance of the <see cref="F3Probe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public F3Probe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "F3_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var overlay = new F3OverlayInspector(Host);
        var offStart = Snapshot();
        await overlay.CaptureAsync("f3-s200-off.png");
        PlaceOnTrigger(4, 3);
        var offTrigger = Snapshot();
        WarpEdgeDriver.Run(Host, "S200", 4, 3, "f3-off-s200-to-s235");
        var offArrival = Snapshot();
        Require(offArrival.Zone == "S235" && offArrival.Position == new NumericsVector2(278, 218) && offArrival.Facing == 3, "F3-off route did not reach manifest S235 arrival");

        Host.State.ZoneId = "S200";
        Host.State.Position = offTrigger.Position;
        Host.State.SetFacing(offTrigger.Facing);
        Host.ApplyScene(Host.GetOrLoadZone("S200"));
        Host.ToggleF3();
        Require(Host.Input.F3On, "F3 did not enable through InputAdapter");
        Require(Snapshot().Zone == offTrigger.Zone && Snapshot().Position == offTrigger.Position && Snapshot().Facing == offTrigger.Facing, "F3 changed simulation before movement");
        await overlay.CaptureAsync("f3-s200-on.png");
        var circlePixels = await overlay.CountPlayerCollisionShapePixelsAsync();
        Require(circlePixels > 40, $"F3 collision shape not rendered (circle_pixels={circlePixels})");
        WarpEdgeDriver.Run(Host, "S200", 4, 3, "f3-on-s200-to-s235");
        var onArrival = Snapshot();
        Require(
            onArrival.Zone == offArrival.Zone && onArrival.Position == offArrival.Position && onArrival.Facing == offArrival.Facing && onArrival.Progress == offArrival.Progress,
            "F3 on/off route changed position, transition, facing, or progress");

        PlaceOnTrigger(1, 7);
        var onTrigger = Snapshot();
        await overlay.CaptureAsync("f3-s235-on.png");
        Host.ToggleF3();
        Require(!Host.Input.F3On, "F3 did not disable through InputAdapter");
        Require(Snapshot() == onTrigger, "F3 changed simulation after warp");
        await overlay.CaptureAsync("f3-s235-off.png");
        QuietLog.Info($"f3 probe PASS off_start={offStart} off_arrival={offArrival} on_arrival={onArrival} overlay_gameplay_delta=false");
    }

    /// <summary>Puts the player on the first cell of a slot in the applied zone with the given facing.</summary>
    /// <param name="slot">Trigger slot.</param>
    /// <param name="facing">Source facing, 0-7.</param>
    private void PlaceOnTrigger(int slot, int facing)
    {
        Host.State.Position = TriggerSlotLocator.FindCell(Host.CurrentZone, slot);
        Host.State.SetFacing(facing);
    }

    /// <summary>Reads the simulation values the overlay must never change.</summary>
    /// <returns>Zone, position, facing, story progress and warp commit count.</returns>
    private (string Zone, NumericsVector2 Position, int Facing, int Progress, int Commits) Snapshot() =>
        (Host.State.ZoneId, Host.State.Position, Host.State.Facing, Host.State.Progress, Host.Warp.CommitCount);
}
