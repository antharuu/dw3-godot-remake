using Dw3Production.Logging;
using Dw3Production.Simulation;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Crosses field exits the way a player does: walk with held pad directions to the trigger cell,
/// enter it with a direction the exit admits, then press Cross on the offer. The driver never
/// assigns a position or a facing, so every arrival comes from the decoded warp target.
/// </summary>
internal sealed class WarpRouteDriver
{
    /// <summary>Upper bound of walking ticks before a route is declared blocked.</summary>
    private const int MaxWalkTicks = 2400;

    /// <summary>Walking ticks between two recorded frames when the recorder samples.</summary>
    private const int WalkFramePeriod = 32;

    /// <summary>Upper bound of admission ticks spent inside the trigger before the offer must appear.</summary>
    private const int MaxAdmissionTicks = 12;

    /// <summary>Upper bound of ticks between Cross and the warp Rearm state.</summary>
    private const int MaxCommitTicks = 16;

    /// <summary>Ticks between two recorded frames during admission and commit when the recorder samples.</summary>
    private const int ShortFramePeriod = 2;

    /// <summary>Running game.</summary>
    private readonly IFieldProbeHost _host;

    /// <summary>Initializes a new instance of the <see cref="WarpRouteDriver"/> class.</summary>
    /// <param name="host">Running game.</param>
    public WarpRouteDriver(IFieldProbeHost host)
    {
        _host = host;
    }

    /// <summary>Walks from the current position into the exit slot until the warp offers it.</summary>
    /// <param name="zoneId">Zone that must be applied when the walk starts.</param>
    /// <param name="slot">Exit slot to reach.</param>
    /// <param name="recorder">Receives the route frames.</param>
    /// <returns>A task that completes when the warp is in the Offer state.</returns>
    /// <exception cref="InvalidOperationException">The route stalled, was blocked or never offered.</exception>
    public async Task WalkToExitAsync(string zoneId, int slot, FrameSequenceRecorder recorder)
    {
        var zone = _host.ActiveZone ?? throw new InvalidOperationException("route has no active zone");
        ProbeRequirement.Require(zone.Zone.Id == zoneId, $"route expected zone={zoneId}");
        var triggerDirection = TriggerSlotLocator.FindDirection(zone, slot);
        var sourceFacing = (triggerDirection + 1) % 8;
        var admissionInput = (SemanticDirection)((sourceFacing + 4) % 8);
        var target = TriggerSlotLocator.FindApproachCell(zone, slot, _host.State.Position);
        QuietLog.Info($"route start zone={zoneId} slot={slot} start=({_host.State.Position.X:0.#},{_host.State.Position.Y:0.#}) target={target} trigdir={triggerDirection} admissionInput={admissionInput}");
        var movementTicks = await WalkIntoOfferRadiusAsync(zone, slot, target, recorder);
        var admissionTicks = await AdmitAsync(admissionInput, recorder);
        ProbeRequirement.Require(_host.Warp.State == WarpState.Offer, $"route did not offer slot={slot} after {movementTicks} walk ticks and {admissionTicks} admission ticks");
        QuietLog.Info($"route offer PASS zone={zoneId} slot={slot} movement_ticks={movementTicks} admission_ticks={admissionTicks} position=({_host.State.Position.X:0.###},{_host.State.Position.Y:0.###}) facing={_host.State.Facing}");
    }

    /// <summary>Presses Cross on the offered exit and checks the committed arrival.</summary>
    /// <param name="expectedZone">Zone the exit leads to.</param>
    /// <param name="expectedPosition">Arrival position decoded for the exit.</param>
    /// <param name="expectedFacing">Arrival facing decoded for the exit.</param>
    /// <param name="recorder">Receives the transition frames.</param>
    /// <returns>A task that completes once the warp is back to Idle in the new zone.</returns>
    /// <exception cref="InvalidOperationException">The warp did not commit, did not rearm or arrived elsewhere.</exception>
    public async Task ActivateOfferedWarpAsync(string expectedZone, NumericsVector2 expectedPosition, int expectedFacing, FrameSequenceRecorder recorder)
    {
        // Release first: a stray engine key event during an awaited frame can leave the action
        // held, and a press while held never raises the one-shot edge that leaves Offer.
        _host.Input.Handle(new InputEventKey { Keycode = Key.Enter, Pressed = false });
        _host.Input.Handle(new InputEventKey { Keycode = Key.Enter, Pressed = true });
        for (var tick = 0; tick < MaxCommitTicks && _host.Warp.State != WarpState.Rearm; tick++)
        {
            _host.TickTestSimulation();
            await recorder.RecordIfDueAsync(tick, ShortFramePeriod);
        }

        ProbeRequirement.Require(_host.Warp.State == WarpState.Rearm, $"input route did not reach {expectedZone} rearm state");
        _host.Input.Handle(new InputEventKey { Keycode = Key.Enter, Pressed = false });
        _host.TickTestSimulation();
        ProbeRequirement.Require(_host.Warp.State == WarpState.Idle, "input route did not release after warp");
        ProbeRequirement.Require(
            _host.State.ZoneId == expectedZone && _host.State.Position == expectedPosition && _host.State.Facing == expectedFacing,
            $"input route did not commit the manifest {expectedZone} arrival");
        await recorder.RecordAsync();
    }

    /// <summary>Walks with Shift held until the player stands within the offer radius of the slot.</summary>
    /// <param name="zone">Zone being crossed.</param>
    /// <param name="slot">Exit slot to reach.</param>
    /// <param name="target">Approach target next to the slot.</param>
    /// <param name="recorder">Receives the route frames.</param>
    /// <returns>The number of walking ticks spent.</returns>
    /// <exception cref="InvalidOperationException">The walk stalled, was blocked or ended outside the radius.</exception>
    private async Task<int> WalkIntoOfferRadiusAsync(FieldZone zone, int slot, NumericsVector2 target, FrameSequenceRecorder recorder)
    {
        var monitor = new RouteStallMonitor(_host.State.Position);
        var movementTicks = 0;

        // A run step is nearly four map units; walking keeps the final approach from oscillating
        // across the trigger edge.
        _host.Input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = true });
        try
        {
            for (; movementTicks < MaxWalkTicks && !IsNearSlot(zone, slot); movementTicks++)
            {
                if (monitor.HasStalled(movementTicks, _host.State.Position))
                {
                    throw new InvalidOperationException(
                        $"route stagnated slot={slot} position={_host.State.Position} dist={NumericsVector2.Distance(_host.State.Position, target):0.#} target={target} facing={_host.State.Facing} snapshot_age={movementTicks - monitor.SnapshotTick}");
                }

                if (!ProbeSteering.StepToward(_host, target))
                {
                    throw new InvalidOperationException($"route blocked before trigger slot={slot} position={_host.State.Position} target={target}");
                }

                await recorder.RecordIfDueAsync(movementTicks, WalkFramePeriod);
            }

            ProbeRequirement.Require(
                IsNearSlot(zone, slot),
                $"route did not reach trigger radius slot={slot} target={target} after={movementTicks} ticks position={_host.State.Position} dist={NumericsVector2.Distance(_host.State.Position, target):0.#}");
        }
        finally
        {
            _host.SetTestHeldDirection(SemanticDirection.None);
            _host.Input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = false });
        }

        return movementTicks;
    }

    /// <summary>Holds the admitted direction inside the trigger until the warp offers the exit.</summary>
    /// <param name="admissionInput">Pad direction whose facing the exit admits.</param>
    /// <param name="recorder">Receives the admission frames.</param>
    /// <returns>The number of admission ticks spent.</returns>
    private async Task<int> AdmitAsync(SemanticDirection admissionInput, FrameSequenceRecorder recorder)
    {
        var admissionTicks = 0;
        for (; admissionTicks < MaxAdmissionTicks && _host.Warp.State != WarpState.Offer; admissionTicks++)
        {
            _host.SetTestHeldDirection(admissionInput);
            _host.TickTestSimulation();
            await recorder.RecordIfDueAsync(admissionTicks, ShortFramePeriod);
        }

        _host.SetTestHeldDirection(SemanticDirection.None);
        return admissionTicks;
    }

    /// <summary>Tells whether a cell of the slot lies within the warp offer radius of the player.</summary>
    /// <param name="zone">Zone being crossed.</param>
    /// <param name="slot">Exit slot.</param>
    /// <returns><see langword="true"/> when the warp can offer the slot from here.</returns>
    private bool IsNearSlot(FieldZone zone, int slot) => zone.Triggers
        .CellsWithin(_host.State.Position, WarpController.OfferRadiusWorldUnits)
        .Any(c => c.Raw != 0 && (c.Raw & 0x1F) == slot);
}
