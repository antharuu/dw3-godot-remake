using System.Diagnostics;
using Dw3Production.Logging;
using Dw3Production.Simulation;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Measures S200 presentation with the F3 overlay off, then on, under the same held left/right
/// input. With F3 on the steady state must not decode or upload textures, rebuild geometry or
/// refresh the HUD text every frame, and the simulation must end at the same position.
/// </summary>
internal sealed class F3PerformanceProbe : FieldProbe
{
    /// <summary>Unmeasured frames that let caches settle before measuring.</summary>
    private const int WarmFrames = 30;

    /// <summary>Measured frames per phase.</summary>
    private const int MeasuredFrames = 120;

    /// <summary>Length of the held-input pattern: six ticks right, then six ticks left.</summary>
    private const int SweepPeriod = 12;

    /// <summary>Presentation delta of one 60 Hz tick, in seconds.</summary>
    private const double TickSeconds = 1.0 / 60.0;

    /// <summary>Walkable S200 position next to spawn where every phase starts.</summary>
    private static readonly NumericsVector2 Start = new(1000, 236);

    /// <summary>Initializes a new instance of the <see cref="F3PerformanceProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public F3PerformanceProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "F3_PERF_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var off = await MeasurePhaseAsync(false);
        var on = await MeasurePhaseAsync(true);
        Require(on.Work.ImageDecodes == 0 && on.Work.TextureUploads == 0, "F3 steady state decoded or uploaded a texture");
        Require(on.Work.GeometryBuilds <= 1 && on.Work.HudRefreshes < on.Timing.Frames / 2, "F3 rebuilt map geometry or refreshed HUD text per frame");
        Require(
            on.Position == off.Position && on.SimulationTicks == off.SimulationTicks && on.Rejections == 0 && off.Rejections == 0,
            "F3 changed matched held-input simulation displacement or rejected movement");
        QuietLog.Info($"f3 perf PASS off_ms_median={off.Timing.MedianMs:0.###} off_ms_p95={off.Timing.P95Ms:0.###} on_ms_median={on.Timing.MedianMs:0.###} on_ms_p95={on.Timing.P95Ms:0.###} off_fps={off.Timing.Fps:0.0} on_fps={on.Timing.Fps:0.0} displacement=({off.Position.X:0.###},{off.Position.Y:0.###}) sim_ticks={on.SimulationTicks} rejections={on.Rejections} frames={on.Timing.Frames} on_decodes={on.Work.ImageDecodes} on_uploads={on.Work.TextureUploads} on_geometry_builds={on.Work.GeometryBuilds} on_hud_refreshes={on.Work.HudRefreshes}");
    }

    /// <summary>Warms up, then measures one phase with the overlay in the requested state.</summary>
    /// <param name="f3On">Overlay state for the phase.</param>
    /// <returns>The phase metrics.</returns>
    private async Task<PresentationMetrics> MeasurePhaseAsync(bool f3On)
    {
        if (Host.Input.F3On != f3On)
        {
            Host.ToggleF3();
        }

        ResetToStart();
        await RunHeldFramesAsync(WarmFrames, samples: null);
        ResetToStart();
        var baseline = PresentationWork.Capture(Host.Renderer);
        var samples = new List<double>(MeasuredFrames);
        var wall = Stopwatch.StartNew();
        var rejections = await RunHeldFramesAsync(MeasuredFrames, samples);
        Host.SetTestHeldDirection(SemanticDirection.None);
        wall.Stop();
        samples.Sort();
        var timing = new FrameTimingStats(
            MeasuredFrames,
            samples[samples.Count / 2],
            samples[(int)Math.Ceiling(samples.Count * 0.95) - 1],
            MeasuredFrames / wall.Elapsed.TotalSeconds);
        return new PresentationMetrics(timing, Host.State.Position, MeasuredFrames, rejections, PresentationWork.Capture(Host.Renderer).Since(baseline));
    }

    /// <summary>Puts the player back at the phase start, facing source direction 1.</summary>
    private void ResetToStart()
    {
        Host.State.Position = Start;
        Host.State.SetFacing(1);
    }

    /// <summary>Steps and presents frames under the left/right sweep, leaving the last direction held.</summary>
    /// <param name="frames">Frames to run.</param>
    /// <param name="samples">Receives each frame time in milliseconds, or <see langword="null"/> during warm-up.</param>
    /// <returns>The number of refused locomotion steps.</returns>
    private async Task<int> RunHeldFramesAsync(int frames, List<double>? samples)
    {
        var rejections = 0;
        for (var i = 0; i < frames; i++)
        {
            Host.SetTestHeldDirection((i % SweepPeriod) < SweepPeriod / 2 ? SemanticDirection.Right : SemanticDirection.Left);
            var timer = Stopwatch.StartNew();
            Host.SimulateStep(movement => rejections += movement.Rejection is null ? 0 : 1);
            Host.Renderer.Present(Host.State, Host.CurrentZone, Host.Input.F3On, TickSeconds);
            await Host.NextFrameDrawn();
            samples?.Add(timer.Elapsed.TotalMilliseconds);
        }

        return rejections;
    }
}
