using Dw3Production.Logging;
using Dw3Production.Simulation;
using Godot;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Runs 24 held-right locomotion ticks on an open 128x128 synthetic floor, logging each sampled
/// player frame and foot offset, checks that releasing the pad returns to the standing set and
/// captures <c>locomotion-probe.png</c>.
/// </summary>
internal sealed class LocomotionProbe : FieldProbe
{
    /// <summary>Side of the synthetic all-walkable floor, in cells.</summary>
    private const int FloorSize = 128;

    /// <summary>Held-right ticks logged by the probe.</summary>
    private const int RunTicks = 24;

    /// <summary>Animation set of the standing pose.</summary>
    private const int StandingSet = 1;

    /// <summary>Initializes a new instance of the <see cref="LocomotionProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public LocomotionProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "LOCOMOTION_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var playerData = Host.PlayerData ?? throw new InvalidOperationException("player data unavailable");
        byte[] cells = [.. Enumerable.Repeat((byte)1, FloorSize * FloorSize)];
        var probeMap = new FloorMap(FloorSize, FloorSize, cells);
        var state = Host.State;
        state.Position = new NumericsVector2(64, 64);
        state.SetFacing((int)SemanticDirection.Right);
        state.AnimationSet = StandingSet;
        state.AnimationDirection = (int)SemanticDirection.Right;
        state.AnimationTicks = 0;
        state.IsRunning = false;
        Host.Input.State.SetDirections(left: false, right: true, up: false, down: false);
        for (var tick = 0; tick < RunTicks; tick++)
        {
            var result = Locomotion.Tick(state, Host.Input.State.Direction, probeMap, []);
            Require(result.Moved, $"locomotion probe rejected tick={tick} reason={result.Rejection}");
            var sample = PlayerAnimation.Sample(playerData, state);
            QuietLog.Info($"locomotion probe tick={tick} position=({state.Position.X:0.###},{state.Position.Y:0.###}) frame={sample.Frame:X2} offset=({sample.OffsetX},{sample.OffsetY}) foot=({sample.FootGroundX},{sample.FootGroundY})");
            state.AnimationTicks++;
        }

        Host.Input.State.SetDirections(left: false, right: false, up: false, down: false);
        var stopped = Locomotion.Tick(state, Host.Input.State.Direction, probeMap, []);
        Require(!stopped.Moved && state.AnimationSet == StandingSet, "locomotion probe release did not stand");
        Host.Renderer.Present(state, Host.CurrentZone, Host.Input.F3On);
        await Host.NextFrameDrawn();
        var capturePath = Path.Combine(Host.PrepareCaptureDirectory(), "locomotion-probe.png");
        var status = Host.CaptureViewport().SavePng(capturePath);
        Require(status == Error.Ok, $"locomotion capture failed={status}");
        QuietLog.Info($"locomotion probe PASS capture={capturePath}");
    }
}
