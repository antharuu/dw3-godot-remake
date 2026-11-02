using Dw3Production.Conversion;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Checks terrain draw order. The source ordering-table depths (11 background, 4 player, 7 and 3
/// foreground; a larger depth is drawn further back) must map to increasing Godot z-indices, and
/// on S200 and S235 an opaque player texel must win over the background layer and lose to the
/// foreground layer in the rendered frame.
/// </summary>
internal sealed class DepthProbe : FieldProbe
{
    /// <summary>Initializes a new instance of the <see cref="DepthProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public DepthProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "DEPTH_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        Require(
            SourceDepth.ToGodotZIndex(11) < SourceDepth.ToGodotZIndex(7) && SourceDepth.ToGodotZIndex(7) < SourceDepth.ToGodotZIndex(3),
            "source ordering-table depth did not invert for Godot terrain order");
        Require(
            SourceDepth.ToGodotZIndex(7) < SourceDepth.ToGodotZIndex(4) && SourceDepth.ToGodotZIndex(4) < SourceDepth.ToGodotZIndex(3) && SourceDepth.ToGodotZIndex(11) < SourceDepth.ToGodotZIndex(4),
            "player depth 4 is not between source terrain depths");
        var captureRoot = Host.PrepareCaptureDirectory();
        await CaptureDepthOverlap("S200", terrainLayer: 0, captureRoot, "depth-s200-background-overlap.png");
        await CaptureDepthOverlap("S200", terrainLayer: 1, captureRoot, "depth-s200-foreground-overlap.png");
        await CaptureDepthOverlap("S235", terrainLayer: 0, captureRoot, "depth-s235-background-overlap.png");
        await CaptureDepthOverlap("S235", terrainLayer: 1, captureRoot, "depth-s235-foreground-overlap.png");
        QuietLog.Info($"depth probe PASS captures={captureRoot}/depth-s[200|235]-[background|foreground]-overlap.png source=11<7<3 godot=-11<-7<-3 player=-4");
    }

    /// <summary>Places the player over a terrain texel, renders, and checks the colour that wins there.</summary>
    /// <param name="zoneId">Zone to load.</param>
    /// <param name="terrainLayer">0 for the background layer, 1 for the foreground layer.</param>
    /// <param name="captureRoot">Directory that receives the capture.</param>
    /// <param name="fileName">Capture file name.</param>
    /// <returns>A task that completes once the capture is checked and written.</returns>
    private async Task CaptureDepthOverlap(string zoneId, int terrainLayer, string captureRoot, string fileName)
    {
        var playerData = Host.PlayerData ?? throw new InvalidOperationException("depth probe player data unavailable");
        if (terrainLayer is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(terrainLayer));
        }

        var state = Host.State;
        state.ZoneId = zoneId;
        state.AnimationSet = 1;
        state.AnimationDirection = 1;
        state.AnimationTicks = 0;
        Host.ApplyScene(Host.GetOrLoadZone(zoneId));
        var overlap = FindOverlap(playerData, terrainLayer);
        state.Position = overlap.PlayerPosition;
        Host.Renderer.Present(state, Host.CurrentZone, f3On: false);
        await Host.NextFrameDrawn();
        var image = Host.CaptureViewport();
        var projection = FieldScreenProjection.BlockCentres(Host.CurrentZone, state.Position);
        var sampleX = projection.X(overlap.WorldPixel.X);
        var sampleY = projection.Y(overlap.WorldPixel.Y);
        Require(ProbePixels.Contains(image, sampleX, sampleY), $"depth probe sample outside viewport zone={zoneId}");
        var rendered = image.GetPixel(sampleX, sampleY);
        Require(
            ProbePixels.SameRgb(rendered, overlap.ExpectedForeground),
            $"depth probe foreground mismatch zone={zoneId} pixel=({sampleX},{sampleY}) expected={overlap.ExpectedForeground} actual={rendered}");
        var capturePath = Path.Combine(captureRoot, fileName);
        Require(image.SavePng(capturePath) == Error.Ok, $"depth probe capture write failed={capturePath}");
        QuietLog.Info($"depth overlap zone={zoneId} world=({overlap.WorldPixel.X:0},{overlap.WorldPixel.Y:0}) screen=({sampleX},{sampleY}) foreground={overlap.ExpectedForeground} rendered={rendered} capture={capturePath}");
    }

    /// <summary>Loads the sampled player frame and searches the applied zone for an overlap.</summary>
    /// <param name="playerData">Player animation table.</param>
    /// <param name="terrainLayer">0 for the background layer, 1 for the foreground layer.</param>
    /// <returns>The overlap to render.</returns>
    private DepthOverlap FindOverlap(PlayerData playerData, int terrainLayer)
    {
        var zone = Host.CurrentZone;
        var sample = PlayerAnimation.Sample(playerData, Host.State);
        var playerImage = ProbePixels.TryLoad(Host.ResolvePlayerFrame(sample.Frame))
            ?? throw new InvalidOperationException($"depth probe images unreadable zone={zone.Zone.Id} layer={terrainLayer}");
        return DepthOverlapFinder.Find(zone, sample, playerImage, terrainLayer);
    }
}
