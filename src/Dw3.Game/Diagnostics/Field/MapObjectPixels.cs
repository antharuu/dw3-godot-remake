using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Compares a rendered frame with the decoded frame 0 of a map object (water or StageTile sprite)
/// placed at its world position plus frame offset, for the camera of the current player position.
/// </summary>
internal sealed class MapObjectPixels
{
    /// <summary>Half the viewport width in world units (1280 pixels / 3 / 2).</summary>
    private const float HalfViewWidth = 1280f / 6f;

    /// <summary>Half the viewport height in world units (720 pixels / 3 / 2).</summary>
    private const float HalfViewHeight = 720f / 6f;

    /// <summary>Width in texels of the region compared by <see cref="CountChanged"/>.</summary>
    private const int ChangeRegionWidth = 48;

    /// <summary>Height in texels of the region compared by <see cref="CountChanged"/>.</summary>
    private const int ChangeRegionHeight = 36;

    /// <summary>Running game.</summary>
    private readonly IFieldProbeHost _host;

    /// <summary>Initializes a new instance of the <see cref="MapObjectPixels"/> class.</summary>
    /// <param name="host">Running game.</param>
    public MapObjectPixels(IFieldProbeHost host)
    {
        _host = host;
    }

    /// <summary>Tells whether the object frame 0 rectangle overlaps the visible world area.</summary>
    /// <param name="water">Map object.</param>
    /// <returns><see langword="true"/> when part of the object can be on screen.</returns>
    /// <exception cref="InvalidOperationException">The frame image is unreadable.</exception>
    public bool IntersectsViewport(WaterObject water)
    {
        var frame = water.Frames[0];
        var source = Image.LoadFromFile(frame.OpaquePath) ?? throw new InvalidOperationException($"water image unreadable={frame.OpaquePath}");
        var camera = Projection().Camera;
        var left = water.X + frame.OffsetX;
        var top = water.Y + frame.OffsetY;
        return left + source.GetWidth() >= camera.X - HalfViewWidth
            && left <= camera.X + HalfViewWidth
            && top + source.GetHeight() >= camera.Y - HalfViewHeight
            && top <= camera.Y + HalfViewHeight;
    }

    /// <summary>Counts opaque object texels whose block centre pixel shows the texel colour.</summary>
    /// <param name="capture">Rendered viewport.</param>
    /// <param name="water">Map object.</param>
    /// <returns>The number of matching texels; occluded or off-screen texels do not count.</returns>
    /// <exception cref="InvalidOperationException">The frame image is unreadable.</exception>
    public int CountOpaqueSourceMatches(Image capture, WaterObject water)
    {
        var frame = water.Frames[0];
        var source = Image.LoadFromFile(frame.OpaquePath) ?? throw new InvalidOperationException($"water image unreadable={frame.OpaquePath}");
        var projection = Projection();
        var left = projection.X(water.X + frame.OffsetX);
        var top = projection.Y(water.Y + frame.OffsetY);
        var matches = 0;
        foreach (var (x, y, expected) in ProbePixels.OpaqueTexels(source))
        {
            var screenX = left + (x * 3) + 1;
            var screenY = top + (y * 3) + 1;
            if (ProbePixels.Contains(capture, screenX, screenY) && ProbePixels.SameRgb(capture.GetPixel(screenX, screenY), expected))
            {
                matches++;
            }
        }

        return matches;
    }

    /// <summary>Counts block centre pixels that changed between two frames in the 48x36 texel region at the object origin.</summary>
    /// <param name="before">Earlier viewport capture.</param>
    /// <param name="after">Later viewport capture.</param>
    /// <param name="water">Animated map object.</param>
    /// <returns>The number of changed pixels inside the viewport.</returns>
    public int CountChanged(Image before, Image after, WaterObject water)
    {
        var frame = water.Frames[0];
        var projection = Projection();
        var left = projection.X(water.X + frame.OffsetX);
        var top = projection.Y(water.Y + frame.OffsetY);
        var changed = 0;
        for (var y = 0; y < ChangeRegionHeight; y++)
        {
            for (var x = 0; x < ChangeRegionWidth; x++)
            {
                var screenX = left + (x * 3) + 1;
                var screenY = top + (y * 3) + 1;
                if (ProbePixels.Contains(before, screenX, screenY) && ProbePixels.DifferentRgb(before.GetPixel(screenX, screenY), after.GetPixel(screenX, screenY)))
                {
                    changed++;
                }
            }
        }

        return changed;
    }

    /// <summary>Returns the block-corner projection for the current zone and player position.</summary>
    /// <returns>The projection.</returns>
    private FieldScreenProjection Projection() => FieldScreenProjection.BlockCorners(_host.CurrentZone, _host.State.Position);
}
