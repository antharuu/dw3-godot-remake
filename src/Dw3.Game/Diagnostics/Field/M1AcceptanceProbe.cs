using Dw3Production.Logging;
using Dw3Production.Simulation;
using Godot;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// M1 acceptance: map collision in S200 and S235, the source run and walk speeds, the release of
/// held input on focus loss, then S200 -> S235 -> S200 -> S235 with normal input only, exactly one
/// commit per edge, still captures and a 12 fps route video.
/// </summary>
internal sealed class M1AcceptanceProbe : FieldProbe
{
    /// <summary>Held ticks of each speed measurement.</summary>
    private const int SpeedProbeTicks = 6;

    /// <summary>Allowed error of a measured distance, in world units.</summary>
    private const float DistanceTolerance = 0.01f;

    /// <summary>Walkable S200 spawn position where the speed checks and the route start.</summary>
    private static readonly NumericsVector2 Spawn = new(1000, 236);

    /// <summary>Initializes a new instance of the <see cref="M1AcceptanceProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public M1AcceptanceProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "M1_ACCEPTANCE_TEST";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory();
        var videoRoot = Host.PrepareVideoDirectory();
        var frameRoot = Path.Combine(videoRoot, "m1-loop-frames");
        ProbeVideoEncoder.ResetFrameDirectory(frameRoot);

        AssertActualMapCollision("S200", new NumericsVector2(958, 200));
        AssertActualMapCollision("S235", new NumericsVector2(187, 63));
        Host.State.ZoneId = "S200";
        Host.State.Position = Spawn;
        Host.State.SetFacing(1);
        Host.ApplyScene(Host.GetOrLoadZone("S200"));
        var runDistance = AssertRunSpeed();
        AssertWalkSpeedAndFocusRelease(runDistance);

        Host.State.Position = Spawn;
        Host.State.SetFacing(1);
        var frames = new FrameSequenceRecorder(Host, frameRoot);
        await DriveRouteAsync(captureRoot, frames);

        var videoPath = Path.Combine(videoRoot, "m1-loop.mp4");
        ProbeVideoEncoder.Encode(frameRoot, videoPath, ProbeVideoEncoder.AcceptanceFrameRate);
        Require(File.Exists(videoPath) && new FileInfo(videoPath).Length > 0, "ffmpeg produced no m1-loop.mp4");
        QuietLog.Info($"m1 acceptance PASS frames={frames.NextFrame} commits={Host.Warp.CommitCount} zone={Host.State.ZoneId} video={videoPath}");
    }

    /// <summary>Holds right for six ticks and checks the source run distance.</summary>
    /// <returns>The measured run distance in world units.</returns>
    private float AssertRunSpeed()
    {
        var runStart = Host.State.Position;
        Host.SetTestHeldDirection(SemanticDirection.Right);
        TickTimes(SpeedProbeTicks);
        Host.SetTestHeldDirection(SemanticDirection.None);
        var runDistance = Host.State.Position.X - runStart.X;
        var expectedRunDistance = SpeedProbeTicks * Locomotion.SourceRunWorldUnitsPerSimulationTick;
        Require(MathF.Abs(runDistance - expectedRunDistance) < DistanceTolerance, $"held native run distance={runDistance:0.###} expected={expectedRunDistance:0.###}");
        QuietLog.Info($"run probe PASS held_input=right ticks={SpeedProbeTicks} distance={runDistance:0.###} world_units_per_second={runDistance * 60 / SpeedProbeTicks:0.###} expected_source_rate={Locomotion.SourceRunWorldUnitsPerSecond:0.###}");
        return runDistance;
    }

    /// <summary>
    /// Holds Shift+right and checks the walk distance is a quarter of the run, that releasing Shift
    /// restores the run step, and that a focus loss stops the player and clears the held input.
    /// </summary>
    /// <param name="runDistance">Run distance measured over the same number of ticks.</param>
    private void AssertWalkSpeedAndFocusRelease(float runDistance)
    {
        Host.State.Position = Spawn;
        Host.Input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = true });
        Host.SetTestHeldDirection(SemanticDirection.Right);
        var walkStart = Host.State.Position;
        TickTimes(SpeedProbeTicks);
        var walkDistance = Host.State.Position.X - walkStart.X;
        var expectedWalkDistance = SpeedProbeTicks * Locomotion.SourceWalkWorldUnitsPerSimulationTick;
        Require(
            MathF.Abs(walkDistance - expectedWalkDistance) < DistanceTolerance && MathF.Abs((walkDistance * 4) - runDistance) < DistanceTolerance,
            $"held Shift walk distance={walkDistance:0.###} expected={expectedWalkDistance:0.###} run={runDistance:0.###}");
        Host.Input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = false });
        var resumedAt = Host.State.Position.X;
        Host.TickTestSimulation();
        Require(MathF.Abs(Host.State.Position.X - resumedAt - Locomotion.SourceRunWorldUnitsPerSimulationTick) < DistanceTolerance, "Shift release did not restore default run");
        Host.SimulateWindowFocus(false);
        var focusLostAt = Host.State.Position;
        Host.TickTestSimulation();
        Require(
            Host.State.Position == focusLostAt && Host.Input.State.Direction == SemanticDirection.None && !Host.Input.State.WalkHeld,
            "focus loss did not clear held run/walk input");
        Host.SimulateWindowFocus(true);
        QuietLog.Info($"walk probe PASS held_input=shift+right ticks={SpeedProbeTicks} distance={walkDistance:0.###} ratio=0.25 focus_loss_stops=true");
    }

    /// <summary>Drives S200 -> S235 -> S200 -> S235 through native input, recording frames and stills.</summary>
    /// <param name="captureRoot">Directory that receives the stills.</param>
    /// <param name="frames">Recorder of the route video frames.</param>
    /// <returns>A task that completes after the third commit.</returns>
    private async Task DriveRouteAsync(string captureRoot, FrameSequenceRecorder frames)
    {
        var route = new WarpRouteDriver(Host);
        await frames.RecordAsync();
        await Host.CaptureStill(captureRoot, "s200-spawn.png");
        await route.WalkToExitAsync("S200", 4, frames);
        RequireOffer("never offered S200 slot 4");
        await route.ActivateOfferedWarpAsync("S235", new NumericsVector2(278, 218), 3, frames);
        await Host.CaptureStill(captureRoot, "s235-after-transition.png");

        // The return crossing starts at the decoded S235 arrival, then re-enters once more.
        await route.WalkToExitAsync("S235", 1, frames);
        RequireOffer("never offered S235 slot 1");
        await route.ActivateOfferedWarpAsync("S200", new NumericsVector2(672, 378), 7, frames);
        await Host.CaptureStill(captureRoot, "s200-after-return.png");

        await route.WalkToExitAsync("S200", 4, frames);
        RequireOffer("never re-offered S200 slot 4");
        await route.ActivateOfferedWarpAsync("S235", new NumericsVector2(278, 218), 3, frames);
        Require(Host.Warp.CommitCount == 3, $"normal route committed {Host.Warp.CommitCount} times instead of three");
        await Host.CaptureStill(captureRoot, "s235-after-reentry.png");
    }

    /// <summary>Requires the warp to be offering, describing the player state otherwise.</summary>
    /// <param name="what">Failed expectation, completed with the trigger cell under the player.</param>
    private void RequireOffer(string what) => Require(
        Host.Warp.State == WarpState.Offer,
        $"input route {what} raw={Host.CurrentZone.Triggers.CellAt(Host.State.Position)} position={Host.State.Position} facing={Host.State.Facing} state={Host.Warp.State}");

    /// <summary>Checks that a held right step from a known spot is refused by the map floor.</summary>
    /// <param name="zoneId">Zone to load.</param>
    /// <param name="blockedFrom">Position right next to a void cell.</param>
    private void AssertActualMapCollision(string zoneId, NumericsVector2 blockedFrom)
    {
        Host.State.ZoneId = zoneId;
        Host.State.Position = blockedFrom;
        Host.State.SetFacing(1);
        Host.ApplyScene(Host.GetOrLoadZone(zoneId));
        Host.SetTestHeldDirection(SemanticDirection.Right);
        MovementResult result = default;
        Host.SimulateStep(movement => result = movement);
        Host.SetTestHeldDirection(SemanticDirection.None);
        Require(
            result.Rejection == "void-footprint" && Host.State.Position == blockedFrom,
            $"source map obstacle did not hold native input zone={zoneId} position={blockedFrom} rejection={result.Rejection}");
        QuietLog.Info($"collision probe PASS zone={zoneId} held_input=right from={blockedFrom} rejection={result.Rejection}");
    }

    /// <summary>Runs <paramref name="ticks"/> simulation steps that must not be refused.</summary>
    /// <param name="ticks">Number of steps.</param>
    private void TickTimes(int ticks)
    {
        for (var tick = 0; tick < ticks; tick++)
        {
            Host.TickTestSimulation();
        }
    }
}
