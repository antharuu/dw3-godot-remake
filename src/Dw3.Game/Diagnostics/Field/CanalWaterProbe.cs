using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Checks that the S200 canal renders blue. The canal north bank at world (1117, 352) is a grey
/// depth-11 stream tile of the zone's background pack that the field tints with the stage
/// colour (0x54, 0x67, 0x96): about (106, 106, 106) becomes (70, 85, 124), and the quarter-additive
/// animated water object 37 on top brings it to about (127, 144, 185). An untinted grey or a
/// missing layer fails the blue-dominance test.
/// </summary>
internal sealed class CanalWaterProbe : FieldProbe
{
    /// <summary>World column of the sampled canal point.</summary>
    private const int CanalWorldX = 1117;

    /// <summary>World row of the sampled canal point.</summary>
    private const int CanalWorldY = 352;

    /// <summary>Upper bound of walking ticks to the viewpoint.</summary>
    private const int MaxWalkTicks = 600;

    /// <summary>Simulation steps between captures: nine 60 Hz steps cover seven source animation ticks.</summary>
    private const int ShimmerSteps = 9;

    /// <summary>Viewport width in pixels.</summary>
    private const int ViewportWidth = 1280;

    /// <summary>Viewport height in pixels.</summary>
    private const int ViewportHeight = 720;

    /// <summary>
    /// Walkable point south of the S200 spawn; from there the camera shows the sampled canal point
    /// in the lower-right quadrant of the viewport.
    /// </summary>
    private static readonly NumericsVector2 Viewpoint = new(1000, 280);

    /// <summary>Initializes a new instance of the <see cref="CanalWaterProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public CanalWaterProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "CANAL_WATER_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        WalkToViewpoint();
        Require(Host.State.ZoneId == "S200", $"canal probe left S200 at {Host.State.Position}");
        AdvanceShimmer();
        var captureRoot = Host.PrepareCaptureDirectory();
        var sample = await CaptureAndSampleAsync(captureRoot);

        // A second capture after the shimmer advanced is time-separated evidence of the animation.
        AdvanceShimmer();
        var after = await CaptureAsync(Path.Combine(captureRoot, "canal-water-s200-after.png"), "canal water after-capture write failed");
        after.Dispose();

        QuietLog.Info($"canal water PASS zone=S200 named_region=canal_north_bank " +
            $"world=({CanalWorldX},{CanalWorldY}) screen=({sample.ScreenX},{sample.ScreenY}) " +
            $"blue_dominant={sample.Blue}/{sample.Samples} " +
            $"player=({Host.State.Position.X:0},{Host.State.Position.Y:0}) " +
            $"source=S200PACK-depth11-stageColor-0x54_0x67_0x96 " +
            $"captures={captureRoot}/canal-water-s200[-after].png");
    }

    /// <summary>Tells whether a viewport pixel exists.</summary>
    /// <param name="x">Pixel column.</param>
    /// <param name="y">Pixel row.</param>
    /// <returns><see langword="true"/> inside the 1280x720 viewport.</returns>
    private static bool IsOnScreen(int x, int y) => x >= 0 && x < ViewportWidth && y >= 0 && y < ViewportHeight;

    /// <summary>
    /// Samples the 3x3 source pixels around a viewport point (one viewport pixel per 3x3 block)
    /// and counts the blue-dominant ones: blue above red by more than 25 and above green by more
    /// than 15, on the 0-255 scale.
    /// </summary>
    /// <param name="capture">Viewport capture.</param>
    /// <param name="screenX">Viewport column of the sampled point.</param>
    /// <param name="screenY">Viewport row of the sampled point.</param>
    /// <returns>The blue-dominant count and the number of samples inside the viewport.</returns>
    private static (int Blue, int Samples) CountBlueDominant(Image capture, int screenX, int screenY)
    {
        var blue = 0;
        var samples = 0;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var px = screenX + (dx * 3);
                var py = screenY + (dy * 3);
                if (!IsOnScreen(px, py))
                {
                    continue;
                }

                var pixel = capture.GetPixel(px, py);
                samples++;
                blue += pixel.B8 > pixel.R8 + 25 && pixel.B8 > pixel.G8 + 15 ? 1 : 0;
            }
        }

        return (blue, samples);
    }

    /// <summary>
    /// Captures the canal view, projects the canal point with the field camera formula (3 viewport
    /// pixels per world unit around the camera centre at (640, 360)) and requires the pixels
    /// around it to be mostly blue.
    /// </summary>
    /// <param name="captureRoot">Capture directory.</param>
    /// <returns>The projected point and its blue-dominance count.</returns>
    /// <exception cref="InvalidOperationException">The point is off screen or the canal is not blue.</exception>
    private async Task<(int ScreenX, int ScreenY, int Blue, int Samples)> CaptureAndSampleAsync(string captureRoot)
    {
        // Both captures are disposed as soon as they are measured, so no image finalizer is left to
        // race the engine shutdown when the probe quits.
        using var capture = await CaptureAsync(Path.Combine(captureRoot, "canal-water-s200.png"), "canal water capture write failed");
        var camera = CameraController.PositionFor(Host.CurrentZone.Zone.Width, Host.CurrentZone.Zone.Height, new Vector2(Host.State.Position.X, Host.State.Position.Y));
        var screenX = Mathf.RoundToInt(((CanalWorldX - camera.X) * 3f) + 640f);
        var screenY = Mathf.RoundToInt(((CanalWorldY - camera.Y) * 3f) + 360f);
        var where = $"player=({Host.State.Position.X:0},{Host.State.Position.Y:0}) camera=({camera.X:0},{camera.Y:0})";
        Require(IsOnScreen(screenX, screenY), $"canal coord ({CanalWorldX},{CanalWorldY}) maps to screen ({screenX},{screenY}) outside viewport — {where}");
        var (blue, samples) = CountBlueDominant(capture, screenX, screenY);
        Require(samples > 0, $"canal region at screen ({screenX},{screenY}) yielded no valid pixels");
        Require(blue * 2 >= samples, $"canal water not blue: blueCount={blue}/{samples} at screen ({screenX},{screenY}) — {where}");
        return (screenX, screenY, blue, samples);
    }

    /// <summary>Presents with F3 off and writes the frame.</summary>
    /// <param name="path">PNG path.</param>
    /// <param name="failure">Failure message when the PNG cannot be written.</param>
    /// <returns>The captured viewport.</returns>
    private async Task<Image> CaptureAsync(string path, string failure)
    {
        var capture = await ViewportCapture.PresentAsync(Host);
        ViewportCapture.Save(capture, path, failure);
        return capture;
    }

    /// <summary>Walks (Shift held) from the spawn to <see cref="Viewpoint"/>, stopping early when no direction moves the player.</summary>
    private void WalkToViewpoint()
    {
        Host.Input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = true });
        for (var tick = 0; tick < MaxWalkTicks && NumericsVector2.Distance(Host.State.Position, Viewpoint) >= 2f; tick++)
        {
            if (!ProbeSteering.DirectionsToward(Host.State.Position, Viewpoint).Any(Host.TryTestMove))
            {
                break;
            }
        }

        Host.Input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = false });
        Host.SetTestHeldDirection(SemanticDirection.None);
    }

    /// <summary>Advances the simulation so the water animation moves on.</summary>
    private void AdvanceShimmer()
    {
        for (var tick = 0; tick < ShimmerSteps; tick++)
        {
            Host.SimulateStep(_ => { });
        }
    }
}
