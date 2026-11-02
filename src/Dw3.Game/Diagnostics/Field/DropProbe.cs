using Dw3Production.Logging;
using Dw3Production.Simulation;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Cliff drop in S345 slot 1: the trigger is offered, Cross moves the player 16 units sideways and
/// the slot height down with the climb height at the drop height, and the drop then returns the
/// standing pose and control on a landing from which the player can move.
/// </summary>
internal sealed class DropProbe : FieldProbe
{
    /// <summary>Upper bound of ticks on the trigger before the drop must be offered.</summary>
    private const int MaxOfferTicks = 4;

    /// <summary>Upper bound of ticks for the drop to finish.</summary>
    private const int MaxDropTicks = 100;

    /// <summary>Initializes a new instance of the <see cref="DropProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public DropProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "DROP_PROBE";

    /// <inheritdoc/>
    protected override Task ExecuteAsync()
    {
        var state = Host.State;
        var zone = Host.GetOrLoadZone("S345");
        Host.ApplyScene(zone);
        state.ZoneId = "S345";
        var drop = zone.Zone.Drops?.SingleOrDefault(item => item.Slot == 1)
            ?? throw new InvalidOperationException("S345 slot=1 SLOT_DROP missing");
        var (cell, direction) = TriggerSlotLocator.FindClimbTrigger(zone, drop.Slot);
        state.Position = cell;
        state.SetFacing(direction);
        state.AnimationSet = 1;
        state.AnimationDirection = direction;
        Host.Input.State.ReleaseAction();
        TickWhile(MaxOfferTicks, () => Host.Warp.State != WarpState.Offer);
        Require(Host.Warp.State == WarpState.Offer && state.Offer, "S345 drop trigger was not offered");

        var start = state.Position;
        Host.Input.State.PressAction();
        Host.TickTestSimulation();
        Require(Host.Drop.IsActive && state.ClimbActive, "Cross did not start S345 drop");
        var expected = start + new NumericsVector2(direction == 7 ? 16 : -16, drop.Height);
        Require(state.Position == expected && state.ClimbHeight == drop.Height << 8, $"drop entry mismatch actual={state.Position} expected={expected}");
        Host.Input.State.ReleaseAction();
        TickWhile(MaxDropTicks, () => Host.Drop.IsActive);
        Require(!Host.Drop.IsActive && !state.ClimbActive && state.ClimbHeight == 0 && state.AnimationSet == 1, "S345 drop did not restore stand/control");
        Require(state.Position != start, "drop landing did not leave its source position");
        var movedDirections = LandingMovementCheck.Run(Host, drop.Slot);
        Require(movedDirections > 0, $"drop landing stuck at {state.Position}");
        QuietLog.Info($"drop probe PASS zone=S345 slot={drop.Slot} height={drop.Height} dir={direction} final=({state.Position.X},{state.Position.Y})");
        return Task.CompletedTask;
    }

    /// <summary>Runs simulation steps while <paramref name="condition"/> holds, at most <paramref name="maxTicks"/>.</summary>
    /// <param name="maxTicks">Upper bound of steps.</param>
    /// <param name="condition">Checked before each step.</param>
    private void TickWhile(int maxTicks, Func<bool> condition)
    {
        for (var tick = 0; tick < maxTicks && condition(); tick++)
        {
            Host.TickTestSimulation();
        }
    }
}
