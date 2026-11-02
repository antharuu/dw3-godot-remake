using Dw3Production.Conversion;
using Dw3Production.Logging;
using Dw3Production.Simulation;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Drives a climb slot through its normal entry points: stand on the trigger until the warp offers
/// it, press Cross to start the climb, then hold Up or Down on the pad until the climb releases
/// control. Each run ends with a <see cref="LandingMovementCheck"/> of the landing.
/// </summary>
internal sealed class ClimbDriver
{
    /// <summary>Upper bound of ticks on the trigger before the climb must be offered.</summary>
    private const int MaxOfferTicks = 4;

    /// <summary>Upper bound of held ticks for a climb to finish.</summary>
    private const int MaxClimbTicks = 400;

    /// <summary>Running game.</summary>
    private readonly IFieldProbeHost _host;

    /// <summary>Directory that receives the climb stills.</summary>
    private readonly string _captureRoot;

    /// <summary>Initializes a new instance of the <see cref="ClimbDriver"/> class.</summary>
    /// <param name="host">Running game, with the climb zone applied.</param>
    /// <param name="captureRoot">Directory that receives the climb stills.</param>
    public ClimbDriver(IFieldProbeHost host, string captureRoot)
    {
        _host = host;
        _captureRoot = captureRoot;
    }

    /// <summary>Climbs a wall all the way, capturing before, halfway and after.</summary>
    /// <param name="zone">Applied zone.</param>
    /// <param name="climb">Climb slot.</param>
    /// <param name="climbUp"><see langword="true"/> to hold Up, <see langword="false"/> to hold Down.</param>
    /// <returns>The heights reached and the landing.</returns>
    public async Task<ClimbProbeResult> DriveAsync(FieldZone zone, Climb climb, bool climbUp)
    {
        var state = _host.State;
        var direction = PlaceAndWaitForOffer(zone, climb.Slot);
        Require(_host.Warp.State == WarpState.Offer && state.Offer, $"climb slot={climb.Slot} never offered raw={zone.Triggers.CellAt(state.Position)} facing={state.Facing}");
        var kind = climbUp ? "up" : "down";
        await _host.CaptureStill(_captureRoot, $"climb-{kind}-before.png");
        StartFromOffer(climb.Slot);

        var range = new ClimbHeightRange();
        var sampled = 0;
        _host.SetTestHeldDirection(climbUp ? SemanticDirection.Up : SemanticDirection.Down);
        for (var tick = 0; tick < MaxClimbTicks && state.ClimbActive; tick++)
        {
            TickTracked(range);
            if (sampled++ == climb.Height / 2)
            {
                await _host.CaptureStill(_captureRoot, $"climb-{kind}-mid.png");
            }
        }

        _host.SetTestHeldDirection(SemanticDirection.None);
        Require(!state.ClimbActive, $"climb slot={climb.Slot} did not finish after 400 ticks");
        await _host.CaptureStill(_captureRoot, $"climb-{kind}-after.png");
        QuietLog.Info($"climb drive slot={climb.Slot} type={kind} max={range.Max} min={range.Min} final=({state.Position.X},{state.Position.Y}) facing={state.Facing}");
        LandingMovementCheck.Run(_host, climb.Slot);
        return new ClimbProbeResult(range.Max, range.Min, state.Position, state.Facing, direction);
    }

    /// <summary>
    /// Climbs halfway, then holds the opposite direction until the climb ends, reproducing the
    /// report of a player stuck after reversing a climb.
    /// </summary>
    /// <param name="zone">Applied zone.</param>
    /// <param name="climb">Climb slot.</param>
    /// <param name="climbUp"><see langword="true"/> to start with Up, <see langword="false"/> to start with Down.</param>
    /// <returns>The heights reached and the landing.</returns>
    public async Task<ClimbProbeResult> DriveReversalAsync(FieldZone zone, Climb climb, bool climbUp)
    {
        var state = _host.State;
        var direction = PlaceAndWaitForOffer(zone, climb.Slot);
        Require(_host.Warp.State == WarpState.Offer && state.Offer, $"climb reversal slot={climb.Slot} never offered");
        StartFromOffer(climb.Slot);

        var range = new ClimbHeightRange();
        _host.SetTestHeldDirection(climbUp ? SemanticDirection.Up : SemanticDirection.Down);
        for (var tick = 0; tick < climb.Height / 2 && state.ClimbActive; tick++)
        {
            TickTracked(range);
        }

        _host.SetTestHeldDirection(climbUp ? SemanticDirection.Down : SemanticDirection.Up);
        for (var tick = 0; tick < MaxClimbTicks && state.ClimbActive; tick++)
        {
            TickTracked(range);
        }

        _host.SetTestHeldDirection(SemanticDirection.None);
        Require(!state.ClimbActive, $"climb reversal slot={climb.Slot} did not finish after reverse");
        await _host.CaptureStill(_captureRoot, climbUp ? "climb-reverse-up.png" : "climb-reverse-down.png");
        QuietLog.Info($"climb reversal slot={climb.Slot} type={(climbUp ? "up" : "down")} max={range.Max} min={range.Min} final=({state.Position.X},{state.Position.Y}) facing={state.Facing}");
        LandingMovementCheck.Run(_host, climb.Slot);
        return new ClimbProbeResult(range.Max, range.Min, state.Position, state.Facing, direction);
    }

    /// <summary>Stands on the slot trigger, standing pose and admitted facing, and ticks until the offer appears.</summary>
    /// <param name="zone">Applied zone.</param>
    /// <param name="slot">Climb slot.</param>
    /// <returns>The trigger direction of the slot.</returns>
    private int PlaceAndWaitForOffer(FieldZone zone, int slot)
    {
        var state = _host.State;
        var (cell, direction) = TriggerSlotLocator.FindClimbTrigger(zone, slot);
        state.Position = cell;
        state.SetFacing(direction);
        state.AnimationSet = 1;
        state.AnimationDirection = direction;
        state.AnimationTicks = 0;
        _host.SetTestHeldDirection(SemanticDirection.None);
        _host.Input.State.ReleaseAction();
        for (var tick = 0; tick < MaxOfferTicks && _host.Warp.State != WarpState.Offer; tick++)
        {
            _host.TickTestSimulation();
        }

        return direction;
    }

    /// <summary>Presses Cross for one tick on the offered slot and requires the climb to start.</summary>
    /// <param name="slot">Climb slot, for the failure message.</param>
    private void StartFromOffer(int slot)
    {
        _host.Input.State.PressAction();
        _host.TickTestSimulation();
        Require(_host.State.ClimbActive, $"cross press did not start climb slot={slot}");
        _host.Input.State.ReleaseAction();
    }

    /// <summary>Runs one simulation step and records the climb height it reached.</summary>
    /// <param name="range">Height range being tracked.</param>
    private void TickTracked(ClimbHeightRange range)
    {
        _host.TickTestSimulation();
        range.Observe(_host.State.ClimbHeight);
    }
}
