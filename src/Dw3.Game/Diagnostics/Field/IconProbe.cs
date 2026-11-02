using Dw3Production.Conversion;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Checks the connection icon drawn above the player: a decoded red and blue frame renders at
/// spawn, the icon glides after the player instead of jumping with it, settles back onto the
/// player once movement stops, and its idle animation shows at least two decoded frames.
/// </summary>
internal sealed class IconProbe : FieldProbe
{
    /// <summary>Simulation steps the player moves right before the trail is measured.</summary>
    private const int MoveSteps = 6;

    /// <summary>Upper bound of steps for the icon to settle onto the stopped player.</summary>
    private const int MaxSettleSteps = 120;

    /// <summary>Distance in world units under which the icon counts as settled.</summary>
    private const float SettleDistance = 0.5f;

    /// <summary>Upper bound of steps to observe a second animation frame.</summary>
    private const int MaxAnimationSteps = 60;

    /// <summary>Initializes a new instance of the <see cref="IconProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public IconProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "ICON_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var iconData = Host.IconData ?? throw new InvalidOperationException("icon data unavailable");
        var captureRoot = Host.PrepareCaptureDirectory();
        Require(
            Host.State.ZoneId == "S200" && Host.State.Position == new NumericsVector2(1000, 236),
            "icon probe must begin at the source S200 spawn");
        var startFrame = Host.Icon.View;
        Require(iconData.Frames.ContainsKey(startFrame.Frame), $"icon view frame 0x{startFrame.Frame:X} is not a decoded frame");
        var imageA = await CaptureAsync(captureRoot, "icon-a.png");
        var countA = CountIconPixels(imageA, iconData.Frames[startFrame.Frame], startFrame.Position);
        Require(countA.Matched > 0, "no decoded icon pixel rendered above the player");
        Require(
            countA.Red > 0 && countA.Blue > 0,
            $"icon frame 0x{startFrame.Frame:X} did not render red+blue pixels above the player red={countA.Red} blue={countA.Blue}");

        var (playerDx, iconDx) = MoveRightAndMeasureTrail();
        var settled = WaitUntilSettled();
        var observed = ObserveAnimationFrames(startFrame.Frame);

        var imageB = await CaptureAsync(captureRoot, "icon-b.png");
        var view = Host.Icon.View;
        var countB = CountIconPixels(imageB, iconData.Frames[view.Frame], view.Position);
        Require(
            countB.Red > 0 && countB.Blue > 0,
            $"settled icon frame 0x{view.Frame:X} did not render red+blue pixels red={countB.Red} blue={countB.Blue}");
        QuietLog.Info($"icon probe PASS frame_a=0x{startFrame.Frame:X} red={countA.Red} blue={countA.Blue} player_dx={playerDx:0.###} icon_dx={iconDx:0.###} settled={settled} frames={FormatFrames(observed)} captures={captureRoot}/icon-[a|b].png");
    }

    /// <summary>Lists frame ids in ascending order as hexadecimal.</summary>
    /// <param name="frames">Observed frame ids.</param>
    /// <returns>A comma-separated list such as <c>0x23,0x24</c>.</returns>
    private static string FormatFrames(IEnumerable<int> frames) =>
        string.Join(",", frames.OrderBy(frame => frame).Select(frame => $"0x{frame:X}"));

    /// <summary>
    /// Holds right for a few steps. The icon follows with an exponential catch-up, so while the
    /// player moves it must trail behind: moved, but less than the player.
    /// </summary>
    /// <returns>The horizontal distance moved by the player and by the icon, in world units.</returns>
    /// <exception cref="InvalidOperationException">The player did not move or the icon did not trail it.</exception>
    private (float PlayerDx, float IconDx) MoveRightAndMeasureTrail()
    {
        var origin = Host.State.Position;
        Host.SetTestHeldDirection(SemanticDirection.Right);
        for (var tick = 0; tick < MoveSteps; tick++)
        {
            Host.TickTestSimulation();
        }

        Host.SetTestHeldDirection(SemanticDirection.None);
        var playerDx = Host.State.Position.X - origin.X;
        var iconDx = Host.Icon.View.Position.X - origin.X;
        Require(playerDx > 0, "player did not move right");
        Require(iconDx > 0 && iconDx < playerDx, $"icon did not trail the player smoothly icon_dx={iconDx:0.###} player_dx={playerDx:0.###}");
        return (playerDx, iconDx);
    }

    /// <summary>Steps the stopped player until the icon has caught up with it.</summary>
    /// <returns>Always <see langword="true"/>; a failure throws.</returns>
    /// <exception cref="InvalidOperationException">The icon did not settle within the step bound.</exception>
    private bool WaitUntilSettled()
    {
        var settled = false;
        for (var tick = 0; tick < MaxSettleSteps && !settled; tick++)
        {
            Host.TickTestSimulation();
            settled = NumericsVector2.Distance(Host.Icon.View.Position, Host.State.Position) < SettleDistance;
        }

        Require(settled, "icon did not settle onto the player after movement stopped");
        return settled;
    }

    /// <summary>Steps until the idle animation has shown a second decoded frame.</summary>
    /// <param name="startFrame">Frame shown at spawn.</param>
    /// <returns>The distinct frames seen, the start frame included.</returns>
    /// <exception cref="InvalidOperationException">The animation did not advance within the step bound.</exception>
    private HashSet<int> ObserveAnimationFrames(int startFrame)
    {
        var observed = new HashSet<int> { startFrame };
        for (var tick = 0; tick < MaxAnimationSteps && observed.Count < 2; tick++)
        {
            Host.TickTestSimulation();
            observed.Add(Host.Icon.View.Frame);
        }

        Require(observed.Count >= 2, $"icon animation did not advance observed_frames={FormatFrames(observed)}");
        return observed;
    }

    /// <summary>Presents the field with the current icon view, F3 off, and writes the frame.</summary>
    /// <param name="captureRoot">Capture directory.</param>
    /// <param name="fileName">PNG file name.</param>
    /// <returns>The captured viewport.</returns>
    private async Task<Image> CaptureAsync(string captureRoot, string fileName)
    {
        Host.Renderer.Present(Host.State, Host.CurrentZone, f3On: false, iconView: Host.Icon.View);
        await Host.NextFrameDrawn();
        var image = Host.CaptureViewport();
        var path = Path.Combine(captureRoot, fileName);
        ViewportCapture.Save(image, path, $"icon capture write failed={path}");
        return image;
    }

    /// <summary>
    /// Compares each opaque texel of a decoded icon frame with the centre of its 3x3 viewport block
    /// and counts the matches, split into red texels (R above 150, G and B below 120) and blue
    /// texels (B above 150, R below 120).
    /// </summary>
    /// <param name="capture">Viewport capture.</param>
    /// <param name="frame">Decoded icon frame, with its offset from the icon anchor.</param>
    /// <param name="iconAnchor">Icon anchor in world units.</param>
    /// <returns>The matched, red and blue texel counts.</returns>
    /// <exception cref="InvalidOperationException">The frame image is unreadable.</exception>
    private IconPixelCount CountIconPixels(Image capture, PlayerIconFrame frame, NumericsVector2 iconAnchor)
    {
        var source = Image.LoadFromFile(frame.Path);
        if (source is null || source.IsEmpty())
        {
            throw new InvalidOperationException($"icon frame image unreadable: {frame.Path}");
        }

        var camera = CameraController.PositionFor(Host.CurrentZone.Zone.Width, Host.CurrentZone.Zone.Height, new Vector2(Host.State.Position.X, Host.State.Position.Y));
        var left = Mathf.RoundToInt(((iconAnchor.X + frame.OffsetX - camera.X) * 3f) + 640f);
        var top = Mathf.RoundToInt(((iconAnchor.Y + frame.OffsetY - camera.Y) * 3f) + 360f);
        var count = new IconPixelCount(0, 0, 0);
        foreach (var (x, y, expected) in ProbePixels.OpaqueTexels(source))
        {
            var screenX = left + (x * 3) + 1;
            var screenY = top + (y * 3) + 1;
            if (ProbePixels.Contains(capture, screenX, screenY) && ProbePixels.SameRgb(capture.GetPixel(screenX, screenY), expected))
            {
                count = count.Add(expected);
            }
        }

        return count;
    }
}
