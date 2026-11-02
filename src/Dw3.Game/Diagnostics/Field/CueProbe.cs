using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Checks the exit cue above the player along S200 -> S235 -> S200 -> S235 with normal input: no
/// cue before an offer, the decoded cue frame drawn at the actor anchor once the offer opens, no cue
/// after a commit, and the cue again on the repeated S200 offer.
/// </summary>
internal sealed class CueProbe : FieldProbe
{
    /// <summary>Upper bound of ticks for the cue to reach its open phase after an offer.</summary>
    private const int MaxCueOpenTicks = 20;

    /// <summary>Height of the cue anchor above the player foot, in world units.</summary>
    private const int CueAnchorHeight = 27;

    /// <summary>Initializes a new instance of the <see cref="CueProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public CueProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "CUE_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory();
        var route = new WarpRouteDriver(Host);
        var absent = await CaptureAsync(captureRoot, "cue-absent-before-offer.png");
        Require(Host.Renderer.CueSnapshot.Phase == ExitCuePhase.Hidden, "cue appears before an exit offer");

        await route.WalkToExitAsync("S200", 4, Frames(captureRoot, 0));
        Require(Host.Warp.State == WarpState.Offer && Host.State.Offer, "normal input did not offer S200 exit for cue");
        TickUntilCueOpens();
        var present = await CaptureAsync(captureRoot, "cue-present-s200-offer.png");
        AssertCueFramebuffer(absent, present, "S200 initial offer");

        await route.ActivateOfferedWarpAsync("S235", new NumericsVector2(278, 218), 3, Frames(captureRoot, 1));
        var removed = await CaptureAsync(captureRoot, "cue-removed-after-commit.png");
        Require(Host.Renderer.CueSnapshot.Phase == ExitCuePhase.Hidden, "cue survives scene commit");

        await route.WalkToExitAsync("S235", 1, Frames(captureRoot, 2));
        Require(Host.Warp.State == WarpState.Offer, "normal input did not offer S235 return exit");
        await route.ActivateOfferedWarpAsync("S200", new NumericsVector2(672, 378), 7, Frames(captureRoot, 3));
        await route.WalkToExitAsync("S200", 4, Frames(captureRoot, 4));
        Require(Host.Warp.State == WarpState.Offer && Host.State.Offer, "normal input did not re-offer S200 exit");
        TickUntilCueOpens();
        var reoffered = await CaptureAsync(captureRoot, "cue-reoffered-s200.png");
        AssertCueFramebuffer(removed, reoffered, "S200 repeat offer");
        await route.ActivateOfferedWarpAsync("S235", new NumericsVector2(278, 218), 3, Frames(captureRoot, 5));
        Require(Host.Warp.CommitCount == 3 && Host.State.ZoneId == "S235", "cue route did not retain exactly three commits");
        Require(!Host.Input.F3On, "cue probe enabled F3");
        QuietLog.Info($"cue probe PASS commits={Host.Warp.CommitCount} f3={Host.Input.F3On} captures={captureRoot}/cue-[absent-before-offer|present-s200-offer|removed-after-commit|reoffered-s200].png");
    }

    /// <summary>Creates the sampled frame recorder of one route leg; each leg restarts at its own frame number.</summary>
    /// <param name="captureRoot">Directory that receives the frames.</param>
    /// <param name="firstFrame">Number of the first frame of the leg.</param>
    /// <returns>The recorder.</returns>
    private FrameSequenceRecorder Frames(string captureRoot, int firstFrame) => new(Host, captureRoot, firstFrame: firstFrame);

    /// <summary>Runs simulation steps until the cue is open, at most <see cref="MaxCueOpenTicks"/>.</summary>
    private void TickUntilCueOpens()
    {
        for (var tick = 0; tick < MaxCueOpenTicks && Host.Renderer.CueSnapshot.Phase != ExitCuePhase.Open; tick++)
        {
            Host.TickTestSimulation();
        }
    }

    /// <summary>Presents with F3 off and writes the frame.</summary>
    /// <param name="captureRoot">Capture directory.</param>
    /// <param name="fileName">PNG file name.</param>
    /// <returns>The captured viewport.</returns>
    private async Task<Image> CaptureAsync(string captureRoot, string fileName)
    {
        var image = await ViewportCapture.PresentAsync(Host);
        var path = Path.Combine(captureRoot, fileName);
        ViewportCapture.Save(image, path, $"cue capture write failed={path}");
        return image;
    }

    /// <summary>
    /// Checks that the cue pop frame changed the framebuffer inside its source bounds, that at least
    /// one opaque cue texel shows its own colour, and that the cue shows the offered trigger direction.
    /// </summary>
    /// <param name="absent">Capture without the cue.</param>
    /// <param name="present">Capture with the cue open.</param>
    /// <param name="label">Offer name used in failure messages.</param>
    private void AssertCueFramebuffer(Image absent, Image present, string label)
    {
        var frame = Host.Renderer.CurrentCuePopFrame ?? throw new InvalidOperationException($"{label}: cue pop frame missing");
        var cueImage = ProbePixels.TryLoad(frame.Path) ?? throw new InvalidOperationException($"{label}: cue source image unreadable");
        var projection = FieldScreenProjection.BlockCentres(Host.CurrentZone, Host.State.Position);
        var originX = projection.X(Host.State.Position.X + frame.OffsetX);
        var originY = projection.Y(Host.State.Position.Y - CueAnchorHeight + frame.OffsetY);
        var matched = 0;
        var changed = 0;
        foreach (var (x, y, expected) in ProbePixels.OpaqueTexels(cueImage))
        {
            var screenX = originX + (x * 3);
            var screenY = originY + (y * 3);
            if (!ProbePixels.Contains(present, screenX, screenY))
            {
                continue;
            }

            var actual = present.GetPixel(screenX, screenY);
            changed += ProbePixels.DifferentRgb(absent.GetPixel(screenX, screenY), actual) ? 1 : 0;
            matched += ProbePixels.SameRgb(actual, expected) ? 1 : 0;
        }

        Require(changed > 0, $"{label}: framebuffer did not change inside source cue anchor bounds");
        Require(matched > 0, $"{label}: no opaque source cue pixel rendered at actor anchor/depth");
        Require(
            Host.Renderer.CueSnapshot.Phase == ExitCuePhase.Open && Host.Renderer.CueSnapshot.RawDirection == Host.State.OfferedTriggerDirection,
            $"{label}: cue state is not the offered raw trigger direction");
    }
}
