using Dw3Production.Logging;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Captures and inspects the F3 collision overlay: the yellow player collision circle and the
/// orange outline of exits whose story condition is not evaluated yet.
/// </summary>
internal sealed class F3OverlayInspector
{
    /// <summary>Extra pixels searched around the collision circle and around an exit cell.</summary>
    private const int SearchMargin = 3;

    /// <summary>Half side of the square searched around an exit cell, in pixels.</summary>
    private const int ExitMarkerHalfSide = 4;

    /// <summary>Running game.</summary>
    private readonly IFieldProbeHost _host;

    /// <summary>Initializes a new instance of the <see cref="F3OverlayInspector"/> class.</summary>
    /// <param name="host">Running game.</param>
    public F3OverlayInspector(IFieldProbeHost host)
    {
        _host = host;
    }

    /// <summary>Presents the current state with the live F3 setting and writes it to the capture directory.</summary>
    /// <param name="fileName">PNG file name.</param>
    /// <returns>A task that completes once the PNG is written.</returns>
    /// <exception cref="InvalidOperationException">The PNG could not be written.</exception>
    public async Task CaptureAsync(string fileName)
    {
        _host.Renderer.Present(_host.State, _host.CurrentZone, _host.Input.F3On);
        await _host.NextFrameDrawn();
        var capturePath = Path.Combine(_host.PrepareCaptureDirectory(), fileName);
        ProbeRequirement.Require(_host.CaptureViewport().SavePng(capturePath) == Error.Ok, $"F3 capture failed={capturePath}");
        QuietLog.Info($"f3 capture={capturePath} enabled={_host.Input.F3On} zone={_host.State.ZoneId}");
    }

    /// <summary>
    /// Counts pure yellow pixels in a box around the player. The overlay draws the circle at the
    /// simulation collision radius, so a count above a few dozen proves the circle is drawn.
    /// </summary>
    /// <returns>The number of yellow pixels.</returns>
    public async Task<int> CountPlayerCollisionShapePixelsAsync()
    {
        _host.Renderer.Present(_host.State, _host.CurrentZone, _host.Input.F3On);
        await _host.NextFrameDrawn();
        var capture = _host.CaptureViewport();
        var projection = FieldScreenProjection.BlockCorners(_host.CurrentZone, _host.State.Position);
        var centerX = projection.X(_host.State.Position.X);
        var centerY = projection.Y(_host.State.Position.Y);
        var reach = Mathf.RoundToInt(Locomotion.PlayerCollisionRadius * 3f) + SearchMargin;
        return CountInBox(capture, centerX, centerY, reach, pixel => pixel.R8 > 240 && pixel.G8 > 240 && pixel.B8 < 40);
    }

    /// <summary>
    /// Presents with F3 on and counts orange pixels around the first cell of an exit slot. A count
    /// above zero proves the gated exit is drawn apart from unconditional (cyan) exits.
    /// </summary>
    /// <param name="zone">Zone that holds the exit.</param>
    /// <param name="slot">Exit slot.</param>
    /// <returns>The number of orange pixels.</returns>
    public async Task<int> CountUnevaluatedExitMarkerPixelsAsync(FieldZone zone, int slot)
    {
        _host.Renderer.Present(_host.State, zone, f3On: true);
        await _host.NextFrameDrawn();
        var capture = _host.CaptureViewport();
        var cell = TriggerSlotLocator.FindCell(zone, slot);
        var projection = FieldScreenProjection.BlockCorners(zone, _host.State.Position);
        return CountInBox(capture, projection.X(cell.X), projection.Y(cell.Y), ExitMarkerHalfSide, pixel => pixel.R8 > 200 && pixel.G8 >= 100 && pixel.G8 <= 155 && pixel.B8 < 60);
    }

    /// <summary>Counts the pixels of a square box that satisfy <paramref name="match"/>, skipping pixels outside the image.</summary>
    /// <param name="capture">Viewport image.</param>
    /// <param name="centerX">Box centre column.</param>
    /// <param name="centerY">Box centre row.</param>
    /// <param name="reach">Half side of the box; the box spans 2 * reach + 1 pixels.</param>
    /// <param name="match">Colour test.</param>
    /// <returns>The number of matching pixels.</returns>
    private static int CountInBox(Image capture, int centerX, int centerY, int reach, Func<Color, bool> match)
    {
        var matches = 0;
        for (var y = centerY - reach; y <= centerY + reach; y++)
        {
            for (var x = centerX - reach; x <= centerX + reach; x++)
            {
                if (ProbePixels.Contains(capture, x, y) && match(capture.GetPixel(x, y)))
                {
                    matches++;
                }
            }
        }

        return matches;
    }
}
