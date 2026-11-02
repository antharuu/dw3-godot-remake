using Dw3Production.Logging;
using Godot;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Checks S200 map objects at spawn: the visible opaque static object with the most matching texels
/// must render more than 100 of them, and a visible animated object must change more than 10 pixels
/// after nine simulation steps (seven source cycle ticks).
/// </summary>
internal sealed class WaterProbe : FieldProbe
{
    /// <summary>Initializes a new instance of the <see cref="WaterProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public WaterProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "WATER_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var water = Host.Water?.Zones["S200"] ?? throw new InvalidOperationException("S200 water manifest unavailable");
        var captureRoot = Host.PrepareCaptureDirectory();
        var pixels = new MapObjectPixels(Host);
        var before = await CaptureAsync(captureRoot, "water-s200-before.png");
        var staticObject = water.Where(item => item.Cycle == 0 && item.Frames[0].Abr < 0 && pixels.IntersectsViewport(item)).MaxBy(item => pixels.CountOpaqueSourceMatches(before, item))
            ?? throw new InvalidOperationException("no visible opaque source water object");
        var sourceMatches = pixels.CountOpaqueSourceMatches(before, staticObject);
        Require(sourceMatches > 100, $"source water object={staticObject.Index} did not render a meaningful opaque region matches={sourceMatches}");
        var animatedObject = water.FirstOrDefault(item => item.Cycle == 1 && item.Frames.Count > 1 && pixels.IntersectsViewport(item))
            ?? throw new InvalidOperationException("no visible animated source water object");
        for (var tick = 0; tick < 9; tick++)
        {
            Host.SimulateStep(_ => { });
        }

        var after = await CaptureAsync(captureRoot, "water-s200-after.png");
        var changed = pixels.CountChanged(before, after, animatedObject);
        Require(changed > 10, $"source water object={animatedObject.Index} did not change after source-cycle ticks changed={changed}");
        QuietLog.Info($"water probe PASS zone=S200 static_object={staticObject.Index} opaque_source_matches={sourceMatches} animated_object={animatedObject.Index} source_ticks=7 changed_pixels={changed} captures={captureRoot}/water-s200-[before|after].png");
    }

    /// <summary>Presents with F3 off and writes the frame.</summary>
    /// <param name="captureRoot">Capture directory.</param>
    /// <param name="fileName">PNG file name.</param>
    /// <returns>The captured viewport.</returns>
    private async Task<Image> CaptureAsync(string captureRoot, string fileName)
    {
        var image = await ViewportCapture.PresentAsync(Host);
        var path = Path.Combine(captureRoot, fileName);
        ViewportCapture.Save(image, path, $"water capture write failed={path}");
        return image;
    }
}
