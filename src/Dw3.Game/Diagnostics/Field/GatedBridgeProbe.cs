using Dw3Production.Logging;
using Dw3Production.Simulation;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// M3 gated bridge S200 slot 0 -> S205 -> S200, both doors story-gated (raw condition 0x7007, not
/// evaluated). Each edge is offered and committed once, logs <c>exit-condition-not-enforced</c> per
/// door, and the F3 overlay draws the door as an orange unevaluated exit. The player is placed on
/// the trigger directly: the bridge floor is connected to spawn, but the greedy walker cannot
/// follow the cross-city path.
/// </summary>
internal sealed class GatedBridgeProbe : FieldProbe
{
    /// <summary>Upper bound of warp ticks between Cross and Rearm.</summary>
    private const int MaxCommitTicks = 12;

    /// <summary>Initializes a new instance of the <see cref="GatedBridgeProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public GatedBridgeProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "GATED_BRIDGE_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var state = Host.State;
        Require(CityLoopRoute.StandsAtSpawn(Host), "gated bridge must begin at the source S200 spawn");
        Require(Host.GetOrLoadZone("S200").Zone.Exits.FirstOrDefault(exit => exit.Slot == 0) is { IsUnevaluated: true }, "S200 slot0 must be an unevaluated exit");

        await CrossGatedEdgeAsync("S200", 0, "s200-to-s205-bridge");
        Require(state.ZoneId == "S205" && state.Position == new NumericsVector2(776, 224) && state.Facing == 1, "bridge did not arrive in S205 via S200 slot0");

        await CrossGatedEdgeAsync("S205", 0, "s205-to-s200-bridge");
        Require(Host.Warp.CommitCount == 2, $"gated bridge committed {Host.Warp.CommitCount} times instead of two");
        Require(state.ZoneId == "S200" && state.Position == new NumericsVector2(96, 796) && state.Facing == 5, "gated bridge did not return to S200 via S205 slot0");
        Require(!Host.Input.F3On, "gated bridge left F3 enabled");
        QuietLog.Info($"gated bridge PASS commits={Host.Warp.CommitCount} route=S200->S205->S200 final_zone={state.ZoneId} final_position=({state.Position.X:0},{state.Position.Y:0}) facing={state.Facing}");
    }

    /// <summary>Offers a gated door, checks its warning and F3 marker, then commits it.</summary>
    /// <param name="sourceZone">Zone that must be applied.</param>
    /// <param name="slot">Gated exit slot.</param>
    /// <param name="label">Edge name for messages and the capture file.</param>
    /// <returns>A task that completes once the warp is Idle in the target zone.</returns>
    private async Task CrossGatedEdgeAsync(string sourceZone, int slot, string label)
    {
        var current = Host.ActiveZone ?? throw new InvalidOperationException("bridge has no current zone");
        Require(current.Zone.Id == sourceZone, $"bridge expected source={sourceZone}");
        var exit = current.Zone.Exits.FirstOrDefault(item => item.Slot == slot);
        Require(exit is not null && exit.IsUnevaluated, $"{sourceZone} slot{slot} is not an unevaluated exit");

        Host.State.Position = TriggerSlotLocator.FindCell(current, slot);
        Host.State.SetFacing(TriggerSlotLocator.FindDirection(current, slot));
        Host.Input.State.ReleaseAction();
        TickWarp(current);
        Require(Host.Warp.State == WarpState.Offer && Host.State.Offer, $"{label} did not offer");
        Require(Host.Log.Warned($"exit-condition-not-enforced:{sourceZone}:{slot}"), $"{label} did not log exit-condition-not-enforced");

        var overlay = new F3OverlayInspector(Host);
        var orange = await overlay.CountUnevaluatedExitMarkerPixelsAsync(current, slot);
        Require(orange > 0, $"{label} F3 did not render the unevaluated exit marker (orange_pixels={orange})");
        Host.ToggleF3();
        await overlay.CaptureAsync($"{label}-unevaluated.png");
        Host.ToggleF3();

        Host.Input.State.PressAction();
        for (var tick = 0; tick < MaxCommitTicks && Host.Warp.State != WarpState.Rearm; tick++)
        {
            TickWarp(Host.CurrentZone);
        }

        Require(Host.Warp.State == WarpState.Rearm, $"{label} did not reach rearm");
        Host.Input.State.ReleaseAction();
        TickWarp(Host.CurrentZone);
        Require(Host.Warp.State == WarpState.Idle, $"{label} did not rearm after release");
    }

    /// <summary>Runs the warp state machine alone for one tick, with the host warning log.</summary>
    /// <param name="zone">Zone passed to the warp tick.</param>
    private void TickWarp(FieldZone zone) =>
        Host.Warp.Tick(Host.State, Host.Input.State, zone, Host.CreateWarpCallbacks(Host.Log.Warn));
}
